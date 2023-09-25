﻿using Microsoft.Extensions.DependencyInjection;
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
        services.Add(new(typeof(IDataflowPublisher<>), typeof(MessagePipeDataflowPublisher<>), ServiceLifetime.Transient));
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
            .DistinctBy(x => x.GenericTypeArguments[0])
            .ToArray();

        if (interfaces.Length == 0)
        {
            throw new ArgumentException($"Type {implementationType} does not implement IDataflowHandler<T>");
        }

        var options = GetMessagePipeOptionsOrThrow(services);

        var lifetime = options.InstanceLifetime.ToServiceLifetime();
        var existingServiceType = AddServicesForInterfacesOfType(services, implementationType, interfaces, lifetime, useExistingSingleton);
        AddMessageHandlersForDataflowHandlers(interfaces, lifetime, existingServiceType, implementationType);
        return services;
    }

    private static void AddMessageHandlersForDataflowHandlers(Type[] interfaces, ServiceLifetime lifetime, Type serviceType, Type implementationType)
    {
        foreach (var type in interfaces.Select(type => type.GenericTypeArguments[0]))
        {
            Type handlerServiceType = typeof(IAsyncMessageHandler<>).MakeGenericType(type);
            Type handlerImplementationType = typeof(MessagePipeDataflowHandler<>).MakeGenericType(type);
            ServiceDescriptor descriptor = new(handlerServiceType, FactoryHelper.ConvertImplementationTypeUnsafe(sp => ActivatorUtilities.CreateInstance(sp, handlerImplementationType, sp.GetServiceOfType(serviceType, implementationType)!), handlerServiceType), lifetime);
        }
    }

    private static Type AddServicesForInterfacesOfType(IServiceCollection services, Type implementationType, Type[] interfaces, ServiceLifetime lifetime, bool useExistingService)
    {
        if (useExistingService && services.FirstOrDefault(s => s.Lifetime == lifetime && s.GetImplementationType() == implementationType) is { } existingDescriptor)
        {
            return ReuseExistingServiceDescriptor();
        }
        return CreateOnceAndReuseInstance();

        Type ReuseExistingServiceDescriptor()
        {
            var existingServiceType = existingDescriptor.ServiceType;
            foreach (var interfaceType in interfaces)
            {
                ServiceDescriptor descriptor = CreateDelegatingDescriptorToExistingService(interfaceType, lifetime, existingServiceType, implementationType);
                services.TryAddEnumerable(descriptor);
            }
            return existingServiceType;
        }

        Type CreateOnceAndReuseInstance()
        {
            var primaryServiceType = interfaces.First();
            services.AddSingleton(primaryServiceType, implementationType);
            foreach (var interfaceType in interfaces.Skip(1))
            {
                ServiceDescriptor descriptor = CreateDelegatingDescriptorToExistingService(interfaceType, lifetime, primaryServiceType, implementationType);
                services.TryAddEnumerable(descriptor);
            }
            return primaryServiceType;
        }
    }

    private static ServiceDescriptor CreateDelegatingDescriptorToExistingService(Type serviceType, ServiceLifetime lifetime, Type existingServiceType, Type existingImplementationType)
    {
        return new(serviceType, FactoryHelper.ConvertImplementationTypeUnsafe(sp => sp.GetServiceOfType(existingServiceType, existingImplementationType)!, existingImplementationType), lifetime);
    }
}