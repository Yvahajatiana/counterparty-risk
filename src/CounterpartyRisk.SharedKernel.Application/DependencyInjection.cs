using System.Reflection;
using CounterpartyRisk.SharedKernel.Application.Behaviors;
using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.SharedKernel.Application.Security;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CounterpartyRisk.SharedKernel.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the dispatcher, the base pipeline (Logging, Authorization, Validation) and every
    /// handler and validator found in <paramref name="assemblies"/>.
    /// Authorization is structural: it cannot be left out, and the host must register an
    /// <see cref="ICurrentUser"/> (otherwise the first dispatch fails, closed).
    /// The Audit behavior (#10) is appended after this call.
    /// </summary>
    public static IServiceCollection AddCqrs(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        // Fail closed at startup if the authorization matrix is inconsistent (threat model 7.3).
        Policies.EnsureConsistent();

        services.TryAddScoped<IDispatcher, Dispatcher>();
        services.TryAddScoped<RequestScopeHolder>();
        services.TryAddScoped<IRequestScope>(sp => sp.GetRequiredService<RequestScopeHolder>());
        services.TryAddScoped<ILegalEntityScope>(sp => sp.GetRequiredService<RequestScopeHolder>());

        // Order matters: the first registered behavior is the outermost one.
        // Authorization runs before validation so that an unauthorized caller never sees a field error.
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)));

        foreach (var assembly in assemblies)
        {
            RegisterHandlers(services, assembly);
        }

        services.AddValidatorsFromAssemblies(assemblies, ServiceLifetime.Scoped, includeInternalTypes: true);
        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var type in handlerTypes)
        {
            foreach (var contract in type.GetInterfaces().Where(IsHandlerContract))
            {
                RegisterHandler(services, contract, type);
            }
        }
    }

    private static void RegisterHandler(IServiceCollection services, Type contract, Type implementation)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == contract);
        if (existing is null)
        {
            services.AddScoped(contract, implementation);
            return;
        }

        // Same type again: the assembly was scanned twice. A different type would silently win at runtime.
        if (existing.ImplementationType != implementation)
        {
            throw new InvalidOperationException(
                $"Several handlers found for {contract}: {existing.ImplementationType} and {implementation}.");
        }
    }

    private static bool IsHandlerContract(Type type) =>
        type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
}
