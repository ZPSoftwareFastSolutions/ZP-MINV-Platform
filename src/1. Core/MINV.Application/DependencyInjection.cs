using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MINV.Application.Behaviors;
using MINV.Application.Billing;

namespace MINV.Application;

public static class DependencyInjection
{
    /// <summary>Registra MediatR con la tubería: validación → autorización (RBAC y licencias) → auditoría → caso de uso.</summary>
    public static IServiceCollection AddMinvApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuditBehavior<,>));
        });
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var contract in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddTransient(contract, type);
            }
        }
        // V4.1 · Trabajo de la facturación SIAT (envío, recuperación fuera de línea, paquetes, notas y correos): uno por scope
        services.TryAddScoped<SiatWorker>();
        services.TryAddScoped<ISiatWorker>(sp => sp.GetRequiredService<SiatWorker>());
        // B6 · Sesión de la petición (la fijan el inicio de sesión y el autenticador del servidor): cambiar la contraseña cierra
        // las DEMÁS sesiones del usuario y conserva esta
        services.TryAddScoped<Abstractions.ICurrentSession, Abstractions.CurrentSession>();
        return services;
    }
}
