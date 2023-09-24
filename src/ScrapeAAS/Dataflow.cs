using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using MessagePipe;
using ScrapeAAS.Contracts;

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

public static class DataflowExtensions
{
    public static IServiceCollection AddDataFlow(this IServiceCollection services)
    {
        services.AddMessagePipe();
        return services;
    }

    public static IServiceCollection AddDataFlowHandler<TMessage, TImplementation>(this IServiceCollection services)
        where TImplementation : class, IDataflowHandler<TMessage>
    {
        services.AddSingleton<IDataflowHandler<TMessage>, TImplementation>();
        services.AddSingleton<IAsyncMessageHandler<TMessage>>(sp => new MessagePipeDataflowHandler<TMessage>(sp.GetRequiredService<IDataflowHandler<TMessage>>()));
        return services;
    }

}