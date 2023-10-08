using Microsoft.Extensions.DependencyInjection;
using MessagePipe;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace ScrapeAAS;

internal sealed class MessagePipeDataflowPublisher<T> : IDataflowPublisher<T>
{
    private readonly IAsyncPublisher<T> _publisher;

    public MessagePipeDataflowPublisher(IAsyncPublisher<T> publisher, IMessagePipeDataflowSubscriber<T> subscriber)
    {
        _publisher = publisher;
        _ = subscriber;
    }

    public ValueTask PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        return _publisher.PublishAsync(message, cancellationToken);
    }
}

public interface IMessagePipeDataflowSubscriber<T> : IDisposable
{

}

internal sealed class MessagePipeDataflowSubscriber<T> : IMessagePipeDataflowSubscriber<T>
{
    private readonly IDisposable _subscription;

    public MessagePipeDataflowSubscriber(IAsyncSubscriber<T> subscriber, IEnumerable<IAsyncMessageHandler<T>> handlers)
    {
        _subscription = DisposableBag.Create(handlers.Select(handler => subscriber.Subscribe(handler)).ToArray());
    }

    public void Dispose()
    {
        _subscription.Dispose();
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

        var options = services.GetMessagePipeOptionsOrThrow();
        var lifetime = options.RequestHandlerLifetime.ToServiceLifetime();

        services.Add(new(typeof(IDataflowPublisher<>), typeof(MessagePipeDataflowPublisher<>), ServiceLifetime.Transient));
        services.Add(new(typeof(IMessagePipeDataflowSubscriber<>), typeof(MessagePipeDataflowSubscriber<>), lifetime));
        return services;
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

        var options = services.GetMessagePipeOptionsOrThrow();
        var lifetime = options.RequestHandlerLifetime.ToServiceLifetime();
        var existingServiceType = AddServicesForInterfacesOfType(services, implementationType, interfaces, lifetime);
        AddHandlersForDataflowHandlers(services, implementationType, interfaces, existingServiceType, lifetime);
        return services;
    }

    private static void AddHandlersForDataflowHandlers(IServiceCollection services, Type implementationType, Type[] interfaces, Type existingServiceType, ServiceLifetime lifetime)
    {
        foreach (var type in interfaces.Select(type => type.GenericTypeArguments[0]))
        {
            Type handlerServiceType = typeof(IAsyncMessageHandler<>).MakeGenericType(type);
            Type handlerImplementationType = typeof(MessagePipeDataflowHandler<>).MakeGenericType(type);
            ServiceDescriptor descriptor = new(
                handlerServiceType,
                FactoryHelper.ConvertImplementationTypeUnsafe(sp => ActivatorUtilities.CreateInstance(sp, handlerImplementationType, sp.GetServiceOfType(existingServiceType, implementationType)!), handlerImplementationType),
                lifetime);
            services.Add(descriptor);
        }
    }

    private static Type AddServicesForInterfacesOfType(IServiceCollection services, Type implementationType, Type[] interfaces, ServiceLifetime lifetime)
    {
        services.Add(new ServiceDescriptor(implementationType, implementationType, lifetime));
        foreach (var interfaceType in interfaces)
        {
            ServiceDescriptor descriptor = CreateDelegatingDescriptorToExistingService(interfaceType, lifetime, implementationType, implementationType);
            services.Add(descriptor);
        }
        return implementationType;
    }

    private static ServiceDescriptor CreateDelegatingDescriptorToExistingService(Type serviceType, ServiceLifetime lifetime, Type existingServiceType, Type existingImplementationType)
    {
        Debug.Assert(serviceType != existingServiceType);
        return new(
            serviceType,
            FactoryHelper.ConvertImplementationTypeUnsafe(sp => sp.GetServiceOfType(existingServiceType, existingImplementationType)!, existingImplementationType),
            lifetime);
    }
}