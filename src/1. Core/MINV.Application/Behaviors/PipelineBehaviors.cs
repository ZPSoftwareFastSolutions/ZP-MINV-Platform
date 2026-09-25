using System.Reflection;
using System.Text.Json;
using FluentValidation;
using MediatR;
using MINV.Application.Abstractions;
using MINV.Application.Common;
using MINV.Domain.Common;
using MINV.Domain.Iam;

namespace MINV.Application.Behaviors;

/// <summary>1. Validación de entrada (FluentValidation) antes de tocar la base de datos.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var v in validators)
        {
            var result = await v.ValidateAsync(request, cancellationToken);
            errors.AddRange(result.Errors.Select(e => e.ErrorMessage));
        }
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
        return await next();
    }
}

/// <summary>2. Autorización: permiso del usuario (RBAC) y módulo comercial licenciado.</summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser user, ILicenseService licenses)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var type = request.GetType();
        foreach (var m in type.GetCustomAttributes<RequiresModuleAttribute>())
        {
            if (!await licenses.IsModuleActiveAsync(m.ModuleCode, cancellationToken))
            {
                throw new AccessDeniedException($"La empresa no tiene licenciado el módulo {m.ModuleCode}.");
            }
        }
        foreach (var p in type.GetCustomAttributes<RequiresPermissionAttribute>())
        {
            if (!user.IsAuthenticated)
            {
                throw new AccessDeniedException("Inicie sesión para continuar.");
            }
            if (!user.HasPermission(p.Permission))
            {
                throw new AccessDeniedException($"Su rol no tiene el permiso {p.Permission}.");
            }
        }
        return await next();
    }
}

/// <summary>3. Auditoría: todo comando auditable deja una fila en AuditLogs con su resultado (✔ / ✖), incluso si fue
/// rechazado (sucesor de registrarActividad de la V2.1). La auditoría nunca interrumpe la operación.</summary>
public sealed class AuditBehavior<TRequest, TResponse>(IAuditTrail trail) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IAuditableRequest auditable)
        {
            return await next();
        }
        var action = request.GetType().Name.Replace("Command", string.Empty, StringComparison.Ordinal);
        var correlation = UuidV7.NewGuid();
        try
        {
            var response = await next();
            await Write(action, AuditOutcome.Succeeded, auditable, response, null, correlation, cancellationToken);
            return response;
        }
        catch (Exception ex) when (ex is DomainException or RequestValidationException or AccessDeniedException
                                       or NotFoundException or ConcurrencyConflictException)
        {
            await Write(action, AuditOutcome.Rejected, auditable, null, ex.Message, correlation, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await Write(action, AuditOutcome.Failed, auditable, null, ex.Message, correlation, cancellationToken);
            throw;
        }
    }

    private async Task Write(string action, AuditOutcome outcome, IAuditableRequest request, object? response, string? error,
        Guid correlation, CancellationToken ct)
    {
        var details = JsonSerializer.Serialize(new { request = request.AuditDetails, result = response, error }, Json);
        await trail.WriteAsync(new AuditEntry(action, outcome, details, correlation), ct);
    }
}
