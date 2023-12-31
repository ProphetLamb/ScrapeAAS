using Microsoft.Extensions.DependencyInjection;
using MessagePipe;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

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

internal sealed class MessagePipeDataflowSubscriber<T>(IAsyncSubscriber<T> subscriber, IEnumerable<IAsyncMessageHandler<T>> handlers) : IMessagePipeDataflowSubscriber<T>
{
    private readonly IDisposable _subscription = DisposableBag.Create(handlers.Select(handler => subscriber.Subscribe(handler)).ToArray());

    public void Dispose()
    {
        _subscription.Dispose();
    }

}

internal sealed class MessagePipeDataflowHandler<T>(IDataflowHandler<T> handler) : IAsyncMessageHandler<T>
{
    private readonly IDataflowHandler<T> _handler = handler;

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
    internal const string AccessDataflowHandlerTypes = "Scans types for IDataflowHandler<> implementations.";

    /// <summary>
    /// Adds all <see cref="IDataflowHandler{T}"/>s in <see cref="Type"/>s in the <see cref="Assembly.GetEntryAssembly"/> or <see cref="Assembly.GetCallingAssembly"/>.
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns></returns>
    [RequiresUnreferencedCode(AccessDataflowHandlerTypes)]
    public static IScrapeAASConfiguration AddDataflowHandlers(this IScrapeAASConfiguration configuration)
    {
        return AddDataflowHandlers(configuration, Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly());
    }

    /// <summary>
    /// Adds all <see cref="IDataflowHandler{T}"/>s in <see cref="Type"/>s in the <see cref="Assembly"/> of each <see cref="Type"/>.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="typesInAssemblies">The types indicating the assemblies whose types to add.</param>
    /// <returns></returns>
    [RequiresUnreferencedCode(AccessDataflowHandlerTypes)]
    public static IScrapeAASConfiguration AddDataflowHandlers(this IScrapeAASConfiguration configuration, params Type[] typesInAssemblies)
    {
        var assemblies = typesInAssemblies
            .Select(t => t.Assembly)
            .Distinct()
            .ToArray();
        return AddDataflowHandlers(configuration, assemblies);
    }

    /// <summary>
    /// Adds all <see cref="IDataflowHandler{T}"/>s in <see cref="Type"/>s in the assemblies.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="assemblies">The assemblies whose types to add.</param>
    /// <returns></returns>
    [RequiresUnreferencedCode(AccessDataflowHandlerTypes)]
    public static IScrapeAASConfiguration AddDataflowHandlers(this IScrapeAASConfiguration configuration, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            _ = AddDataflowHandlers(configuration, assembly);
        }
        return configuration;
    }

    /// <summary>
    /// Adds all <see cref="IDataflowHandler{T}"/>s in <see cref="Type"/>s in the assembly.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="assembly">The assembly whose types to add.</param>
    [RequiresUnreferencedCode(AccessDataflowHandlerTypes)]
    public static IScrapeAASConfiguration AddDataflowHandlers(this IScrapeAASConfiguration configuration, Assembly assembly)
    {
        ScrapeAASUsecase injector = new(new($"injector-{assembly.GetName().FullName}"), (configuration, services) =>
        {
            var types = GetTypesToInject(assembly);
            foreach (var type in types)
            {
                _ = AddDataflow(configuration, type);
            }
        });
        return configuration.Use(injector);
    }

    [RequiresUnreferencedCode(AccessDataflowHandlerTypes)]
    private static IEnumerable<Type> GetTypesToInject(Assembly assembly)
    {
        var types = assembly
            .GetTypes()
            .Where(t
            => !t.IsAbstract
            && !t.IsInterface
            && t.GetInterfaces().Any(i
                => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IDataflowHandler<>)
                )
            );
        return types;
    }

    /// <summary>
    /// Adds <see cref="MessagePipe"/> powered dataflow capabilities and adds all <see cref="IDataflowHandler{T}"/>s in <see cref="Type"/>s in the <see cref="Assembly"/> of each <see cref="Type"/>.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="messagePipeConfiguration">The <see cref="MessagePipe"/> configuration.</param>
    [RequiresUnreferencedCode(AccessDataflowHandlerTypes)]
    public static IScrapeAASConfiguration UseMessagePipeDataflow(this IScrapeAASConfiguration configuration, Action<MessagePipeOptions>? messagePipeConfiguration = null)
    {
        configuration
            .AddDataflowHandlers()
            .Use(ScrapeAASRole.Dataflow, (configuration, services) =>
            {
                var defaultMessagePipeConfiguration = (MessagePipeOptions options) =>
                {
                    var lifetime = configuration.LongLivingServiceLifetime.ToInstanceLifetime();
                    options.InstanceLifetime = lifetime;
                    options.RequestHandlerLifetime = lifetime;
                };
                _ = services.AddMessagePipe(messagePipeConfiguration ?? defaultMessagePipeConfiguration);

                services.Add(new(typeof(IDataflowPublisher<>), typeof(MessagePipeDataflowPublisher<>), ServiceLifetime.Transient));
                services.Add(new(typeof(IMessagePipeDataflowSubscriber<>), typeof(MessagePipeDataflowSubscriber<>), configuration.LongLivingServiceLifetime));
            });
        return configuration;
    }

    /// <summary>
    /// Registers all a single instance of <typeparamref name="TImplementation"/> for all implemented <see cref="IDataflowHandler{T}"/> interfaces.
    /// </summary>
    /// <typeparam name="TImplementation">The type to register.</typeparam>
    /// <param name="configuration">The service collection.</param>
    /// <exception cref="ArgumentException">Thrown if <typeparamref name="TImplementation"/> does not implement <see cref="IDataflowHandler{T}"/>.</exception>
    public static IScrapeAASConfiguration AddDataflow<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>(this IScrapeAASConfiguration configuration)
        where TImplementation : class
    {
        return configuration.AddDataflow(typeof(TImplementation));
    }

    /// <summary>
    /// Registers all a single instance of <paramref name="implementationType"/> for all implemented <see cref="IDataflowHandler{T}"/> interfaces.
    /// </summary>
    /// <param name="implementationType">The type to register.</param>
    /// <param name="services">The service collection.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="implementationType"/> does not implement <see cref="IDataflowHandler{T}"/>.</exception>
    public static IScrapeAASConfiguration AddDataflow(this IScrapeAASConfiguration configuration, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType)
    {
        configuration.Add((configuration, services) =>
        {
            var interfaces = implementationType.GetInterfaces()
                .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDataflowHandler<>))
                .DistinctBy(x => x.GenericTypeArguments[0])
                .ToArray();

            if (interfaces.Length == 0)
            {
                throw new ArgumentException($"Type {implementationType} does not implement IDataflowHandler<T>");
            }

            var existingServiceType = AddServicesForInterfacesOfType(services, implementationType, interfaces, configuration.LongLivingServiceLifetime);
            AddHandlersForDataflowHandlers(services, implementationType, interfaces, existingServiceType, configuration.LongLivingServiceLifetime);
        });
        return configuration;
    }

    private static void AddHandlersForDataflowHandlers(IServiceCollection services, Type implementationType, Type[] interfaces, Type existingServiceType, ServiceLifetime lifetime)
    {
        foreach (var type in interfaces.Select(type => type.GenericTypeArguments[0]))
        {
            var handlerServiceType = typeof(IAsyncMessageHandler<>).MakeGenericType(type);
            var handlerImplementationType = typeof(MessagePipeDataflowHandler<>).MakeGenericType(type);
            ServiceDescriptor descriptor = new(
                handlerServiceType,
                FactoryHelper.ConvertImplementationTypeUnsafe(sp => ActivatorUtilities.CreateInstance(sp, handlerImplementationType, sp.GetServiceOfType(existingServiceType, implementationType)!), handlerImplementationType),
                lifetime);
            services.Add(descriptor);
        }
    }

    private static Type AddServicesForInterfacesOfType(IServiceCollection services, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType, Type[] interfaces, ServiceLifetime lifetime)
    {
        services.Add(new ServiceDescriptor(implementationType, implementationType, lifetime));
        foreach (var interfaceType in interfaces)
        {
            var descriptor = CreateDelegatingDescriptorToExistingService(interfaceType, lifetime, implementationType, implementationType);
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

    private static InstanceLifetime ToInstanceLifetime(this ServiceLifetime lifetime)
    {
        return lifetime switch
        {
            ServiceLifetime.Singleton => InstanceLifetime.Singleton,
            ServiceLifetime.Scoped => InstanceLifetime.Scoped,
            ServiceLifetime.Transient => InstanceLifetime.Transient,
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime))
        };
    }
}
