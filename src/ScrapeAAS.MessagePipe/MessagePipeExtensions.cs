using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

internal static class MessagePipeExtensions
{
    public static ServiceLifetime ToServiceLifetime(this InstanceLifetime lifetime)
    {
        return lifetime switch
        {
            InstanceLifetime.Singleton => ServiceLifetime.Singleton,
            InstanceLifetime.Scoped => ServiceLifetime.Scoped,
            InstanceLifetime.Transient => ServiceLifetime.Transient,
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null)
        };
    }
    public static MessagePipeOptions GetMessagePipeOptionsOrThrow(this IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(service => service.Lifetime == ServiceLifetime.Singleton && service.ImplementationInstance?.GetType() == typeof(MessagePipeOptions));
        var options = descriptor?.ImplementationInstance as MessagePipeOptions;
        return options ?? throw new InvalidOperationException("MessagePipeOptions not registered.");
    }

}
