using Microsoft.Extensions.DependencyInjection;
using MessagePipe;
using Microsoft.Extensions.Options;

namespace ScrapeAAS;

internal sealed class MessagePipeDataflowPublisher<T> : IDataflowPublisher<T>
{
    private readonly IAsyncPublisher<T> _publisher;

    public MessagePipeDataflowPublisher(IAsyncPublisher<T> publisher)
    {
        _publisher = publisher;
    }

    public ValueTask PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        return _publisher.PublishAsync(message, cancellationToken);
    }
}

internal sealed class MessagePipeDataflowHandler<T> : IAsyncMessageHandler<T>
{
    private readonly IDataflowHandler<T> _handler;

    public MessagePipeDataflowHandler(IDataflowHandler<T> handler)
    {
        _handler = handler;
    }

    public async ValueTask HandleAsync(T message, CancellationToken cancellationToken = default)
    {
        await _handler.HandleAsync(message, cancellationToken);
    }
}

public sealed class DataflowOptions : IOptions<DataflowOptions>
{
    public ServiceLifetime InstanceLifetime { get; set; } = ServiceLifetime.Singleton;
    DataflowOptions IOptions<DataflowOptions>.Value => this;
}

public static class DataflowExtensions
{
    public static IServiceCollection AddDataFlow(this IServiceCollection services, Action<MessagePipeOptions>? messagePipeConfiguration = null)
    {
        services.AddMessagePipe(messagePipeConfiguration ?? delegate { });
        return services;
    }

    private static MessagePipeOptions GetMessagePipeOptionsOrThrow(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(service => service.Lifetime == ServiceLifetime.Singleton && service.ImplementationInstance?.GetType() == typeof(MessagePipeOptions));
        var options = descriptor?.ImplementationInstance as MessagePipeOptions;
        return options ?? throw new InvalidOperationException("MessagePipeOptions not registered.");
    }

    /// <summary>
    /// Registers all a single singleton instance of <typeparamref name="TImplementation"/> for all implemented <see cref="IDataflowHandler{T}"/> interfaces.
    /// </summary>
    /// <typeparam name="TImplementation">The type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="useExistingSingleton">If true, will reuse an existing singleton instance of <paramref name="implementationType"/> if one is already registered.</param>
    /// <exception cref="ArgumentException">Thrown if <typeparamref name="TImplementation"/> does not implement <see cref="IDataflowHandler{T}"/>.</exception>
    public static IServiceCollection AddDataFlow<TImplementation>(this IServiceCollection services, bool useExistingSingleton = false)
        where TImplementation : class
    {
        return services.AddDataFlowHandler(typeof(TImplementation), useExistingSingleton);
    }

    /// <summary>
    /// Registers all a single singleton instance of <paramref name="implementationType"/> for all implemented <see cref="IDataflowHandler{T}"/> interfaces.
    /// </summary>
    /// <param name="implementationType">The type to register.</param>
    /// <param name="services">The service collection.</param>
    /// <param name="useExistingSingleton">If true, will reuse an existing singleton instance of <paramref name="implementationType"/> if one is already registered.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="implementationType"/> does not implement <see cref="IDataflowHandler{T}"/>.</exception>
    public static IServiceCollection AddDataFlowHandler(this IServiceCollection services, Type implementationType, bool useExistingSingleton = false)
    {
        var interfaces = implementationType.GetInterfaces()
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDataflowHandler<>))
            .ToArray();

        if (interfaces.Length == 0)
        {
            throw new ArgumentException($"Type {implementationType} does not implement IDataflowHandler<T>");
        }

        var options = GetMessagePipeOptionsOrThrow(services);

        AddServicesForInterfacesOfType(services, implementationType, interfaces, options.InstanceLifetime.ToServiceLifetime(), useExistingSingleton);
        return services;
    }

    private static void AddServicesForInterfacesOfType(IServiceCollection services, Type implementationType, Type[] interfaces, ServiceLifetime lifetime, bool useExistingService)
    {
        if (useExistingService && FindServiceWithMatchingImplementation(services, lifetime, implementationType) is { } existingDescriptor)
        {
            ReuseExistingServiceDescriptor();
            return;
        }
        CreateOnceAndReuseInstance();
        return;

        static ServiceDescriptor? FindServiceWithMatchingImplementation(IServiceCollection services, ServiceLifetime lifetime, Type implementationType)
        {
            return services.Where(s => s.Lifetime == lifetime && (
                s.ImplementationType == implementationType ||
                s.ImplementationInstance?.GetType() == implementationType ||
                s.ImplementationFactory?.GetType().GenericTypeArguments.LastOrDefault() == implementationType)
            ).FirstOrDefault();
        }

        void ReuseExistingServiceDescriptor()
        {
            if (lifetime == ServiceLifetime.Singleton && existingDescriptor.ImplementationInstance is { } existingInstance)
            {
                foreach (var interfaceType in interfaces)
                {
                    services.Add(new(interfaceType, _ => existingInstance, lifetime));
                }
                return;
            }

            var existingInterface = existingDescriptor.ServiceType;
            foreach (var interfaceType in interfaces)
            {
                services.Add(new(interfaceType, sp => sp.GetServices(existingInterface).First(x => x is not null && x.GetType() == implementationType)!, lifetime));
            }
        }

        void CreateOnceAndReuseInstance()
        {
            var existingInterface = interfaces[0];
            services.AddSingleton(existingInterface, implementationType);
            foreach (var interfaceType in interfaces.Skip(1))
            {
                services.Add(new(interfaceType, sp => sp.GetServices(existingInterface).Where(x => x is not null && x.GetType() == implementationType).Single()!, lifetime));
            }
        }
    }
}