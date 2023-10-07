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
}
