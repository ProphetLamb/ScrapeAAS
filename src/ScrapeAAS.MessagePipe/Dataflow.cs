using Microsoft.Extensions.DependencyInjection;
using MessagePipe;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public ValueTask HandleAsync(T message, CancellationToken cancellationToken = default)
    {
        return _handler.HandleAsync(message, cancellationToken);
    }
}

public sealed class DataflowOptions : IOptions<DataflowOptions>
{
    public ServiceLifetime InstanceLifetime { get; set; } = ServiceLifetime.Singleton;
    DataflowOptions IOptions<DataflowOptions>.Value => this;
}

public static class DataflowExtensions
{
    public static IServiceCollection AddMessagePipeDataFlow(this IServiceCollection services, Action<MessagePipeOptions>? messagePipeConfiguration = null)
    {
        services.AddMessagePipe(messagePipeConfiguration ?? delegate { });

        var options = GetMessagePipeOptionsOrThrow(services);
        var lifetime = options.InstanceLifetime.ToServiceLifetime();
        services.Add(new(typeof(IDataflowPublisher<>), typeof(MessagePipeDataflowPublisher<>), lifetime));
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
    /// <exception cref="ArgumentException">Thrown if <typeparamref name="TImplementation"/> does not implement <see cref="IDataflowHandler{T}"/>.</exception>
    public static IServiceCollection AddDataFlow<TImplementation>(this IServiceCollection services)
        where TImplementation : class
    {
        return services.AddDataFlow(typeof(TImplementation));
    }

    /// <summary>
    /// Registers all a single singleton instance of <paramref name="implementationType"/> for all implemented <see cref="IDataflowHandler{T}"/> interfaces.
    /// </summary>
    /// <param name="implementationType">The type to register.</param>
    /// <param name="services">The service collection.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="implementationType"/> does not implement <see cref="IDataflowHandler{T}"/>.</exception>
    public static IServiceCollection AddDataFlow(this IServiceCollection services, Type implementationType)
    {
        var interfaces = implementationType.GetInterfaces()
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDataflowHandler<>))
            .DistinctBy(x => x.GenericTypeArguments[0])
            .ToArray();

        if (interfaces.Length == 0)
        {
            throw new ArgumentException($"Type {implementationType} does not implement IDataflowHandler<T>");
        }

        var options = GetMessagePipeOptionsOrThrow(services);
        var lifetime = options.InstanceLifetime.ToServiceLifetime();
        var existingServiceType = AddServicesForInterfacesOfType(services, implementationType, interfaces, lifetime);
        AddMessageHandlersForDataflowHandlers(services, implementationType, interfaces, existingServiceType, lifetime);
        return services;
    }

    private static void AddMessageHandlersForDataflowHandlers(IServiceCollection services, Type implementationType, Type[] interfaces, Type serviceType, ServiceLifetime lifetime)
    {
        foreach (var type in interfaces.Select(type => type.GenericTypeArguments[0]))
        {
            Type handlerServiceType = typeof(IAsyncMessageHandler<>).MakeGenericType(type);
            Type handlerImplementationType = typeof(MessagePipeDataflowHandler<>).MakeGenericType(type);
            ServiceDescriptor descriptor = new(handlerServiceType, FactoryHelper.ConvertImplementationTypeUnsafe(sp => ActivatorUtilities.CreateInstance(sp, handlerImplementationType, sp.GetServiceOfType(serviceType, implementationType)!), handlerImplementationType), lifetime);
            services.TryAddEnumerable(descriptor);
        }
    }

    private static Type AddServicesForInterfacesOfType(IServiceCollection services, Type implementationType, Type[] interfaces, ServiceLifetime lifetime)
    {
        var primaryServiceType = interfaces.First();
        services.TryAddEnumerable(new ServiceDescriptor(primaryServiceType, implementationType, lifetime));
        foreach (var interfaceType in interfaces.Skip(1))
        {
            ServiceDescriptor descriptor = CreateDelegatingDescriptorToExistingService(interfaceType, lifetime, primaryServiceType, implementationType);
            services.TryAddEnumerable(descriptor);
        }
        return primaryServiceType;
    }

    private static ServiceDescriptor CreateDelegatingDescriptorToExistingService(Type serviceType, ServiceLifetime lifetime, Type existingServiceType, Type existingImplementationType)
    {
        return new(serviceType, FactoryHelper.ConvertImplementationTypeUnsafe(sp => sp.GetServiceOfType(existingServiceType, existingImplementationType)!, existingImplementationType), lifetime);
    }
}