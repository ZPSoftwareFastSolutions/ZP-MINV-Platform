using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MINV.Application.Behaviors;

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
        return services;
    }
}
