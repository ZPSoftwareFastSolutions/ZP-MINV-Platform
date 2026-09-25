using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.OpenApi.Models;
using MINV.ApiGateway.Background;
using MINV.ApiGateway.Endpoints;
using MINV.ApiGateway.Security;
using MINV.Application;
using MINV.Application.Remote;
using MINV.Domain.Integration;
using MINV.Infrastructure;
using MINV.Infrastructure.Hosting;

namespace MINV.ApiGateway;

/// <summary>
/// V4 · Armado del API Gateway B2B (e-commerce, ERP). Variables: <c>MINV_DB</c> (rol minv_server), <c>MINV_INTEGRATION_KEYS</c>
/// (claves maestras de los secretos de webhooks) y <c>ASPNETCORE_URLS</c>. <c>--Minv:Storage=memoria</c> para pruebas.
/// </summary>
public static class ApiGatewayApp
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddMinvApplication();
        var storage = builder.Services.AddMinvServerStorage(builder.Configuration["Minv:Storage"],
            builder.Configuration[MINV.Infrastructure.DependencyInjection.ConnectionStringVariable]
            ?? Environment.GetEnvironmentVariable(MINV.Infrastructure.DependencyInjection.ConnectionStringVariable));
        builder.Services.AddMinvWebhookDispatcher(builder.Configuration.GetValue("Minv:Webhooks:AllowPrivateTargets", false));
        if (builder.Configuration.GetValue("Minv:Webhooks:Enabled", true))
        {
            builder.Services.AddHostedService<WebhookDispatcherService>();
        }
        if (storage == ServerStorage.Postgres && builder.Configuration.GetValue("Minv:Reporting:Enabled", true))
        {
            builder.Services.AddHostedService<ReportingRefreshService>();
        }

        builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
        builder.Services.AddAuthorization(options =>
        {
            foreach (var (code, _, _) in ApiScopes.All)
            {
                options.AddPolicy(code, p => p.RequireAuthenticatedUser().RequireClaim(ApiKeyAuthenticationHandler.ScopeClaim, code));
            }
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Antes de autenticar: 120 peticiones por minuto por IP (frena el barrido de llaves)
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "?", _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
            // Por llave (el prefijo público del token; el limitador corre ANTES de autenticar): ráfaga de 100 y 10 por segundo
            options.AddPolicy("api-key", http => RateLimitPartition.GetTokenBucketLimiter(
                KeyPartition(http) ?? http.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new TokenBucketRateLimiterOptions { TokenLimit = 100, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromSeconds(1), QueueLimit = 0 }));
        });
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.IncludeFields = true;
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "M-INV API Gateway",
                Version = "v1",
                Description = "API B2B de M-INV para e-commerce y ERP: catálogo, stock por sucursal, pedidos idempotentes, transferencias y webhooks firmados " +
                              "(cabecera X-MINV-Signature: t=<unix>,v1=<HMAC-SHA256 hex de \"t.cuerpo\">).",
            });
            o.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "API Key: Authorization: Bearer minv_<prefijo>_<secreto>",
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } }] = [],
            });
        });

        builder.Services.AddSingleton(new StorageInfo(storage));

        var app = builder.Build();
        // Errores de los casos de uso → ProblemDetails (RFC 7807) con el mismo criterio que el servidor en la nube
        app.UseExceptionHandler(errors => errors.Run(async http =>
        {
            var exception = http.Features.Get<IExceptionHandlerFeature>()?.Error;
            var error = exception is BadHttpRequestException bad
                ? new RpcError(RpcErrorKinds.Validation, bad.Message)
                : RpcCatalog.ToError(exception ?? new InvalidOperationException());
            http.Response.StatusCode = RpcCatalog.HttpStatusOf(error.Kind);
            await Results.Problem(new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = http.Response.StatusCode,
                Title = error.Kind,
                Detail = error.Message,
                Type = $"https://minv.example/errores/{error.Kind}",
                Extensions = { ["errors"] = error.Errors, ["code"] = error.Code },
            }).ExecuteAsync(http);
        }));
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSwagger(o => o.RouteTemplate = "docs/{documentName}/openapi.json");
        app.UseSwaggerUI(o =>
        {
            o.RoutePrefix = "docs";
            o.SwaggerEndpoint("/docs/v1/openapi.json", "M-INV API v1");
            o.DocumentTitle = "M-INV API Gateway";
        });
        app.MapGet("/health", () => Results.Ok(new { status = "ok", product = "M-INV API Gateway", version = ServerHosting.Version })).AllowAnonymous();
        V1Endpoints.Map(app);
        return app;
    }

    /// <summary>Partición del límite por llave: el prefijo público de la API Key (de la cabecera, sin tocar la base).</summary>
    private static string? KeyPartition(HttpContext http)
    {
        var token = http.Request.Headers[ApiKeyAuthenticationHandler.HeaderName].ToString();
        if (string.IsNullOrEmpty(token))
        {
            var header = http.Request.Headers.Authorization.ToString();
            token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : string.Empty;
        }
        return Application.Integration.ApiKeyTokens.PrefixOf(token) is { } prefix ? "key:" + prefix : null;
    }

    public static async Task RunAsync(WebApplication app)
    {
        var storage = app.Services.GetRequiredService<StorageInfo>().Storage;
        if (storage == ServerStorage.Postgres)
        {
            await ServerHosting.VerifyDatabaseAsync(app.Services);
        }
        app.Logger.LogInformation("M-INV ApiGateway {Version} · almacenamiento: {Storage}", ServerHosting.Version, storage);
        await app.RunAsync();
    }
}

/// <summary>Almacenamiento elegido al arrancar.</summary>
public sealed record StorageInfo(ServerStorage Storage);

/// <summary>Marca del ensamblado del gateway.</summary>
public sealed class ApiGatewayMarker;
