using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

public sealed class SingleProxyProviderOptions : IOptions<SingleProxyProviderOptions>
{
    public WebProxy Proxy { get; set; } = new();

    SingleProxyProviderOptions IOptions<SingleProxyProviderOptions>.Value => this;
}

internal class SingleProxyProvider(IOptions<SingleProxyProviderOptions> options) : IProxyProvider
{
    private readonly SingleProxyProviderOptions _options = options.Value;

    public ValueTask<WebProxy> GetProxyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(_options.Proxy);
    }
}

public static class SingleProxyProviderExtensions
{
    public static IScrapeAASConfiguration UseSingleProxyProvider(this IScrapeAASConfiguration configuration, Action<SingleProxyProviderOptions> configure)
    {
        configuration.Use(ScrapeAASRole.ProxyProvider, (configuration, services) =>
        {
            _ = services.Configure(configure);
            services.Add(new(typeof(IProxyProvider), typeof(SingleProxyProvider), configuration.LongLivingServiceLifetime));
        });
        return configuration;
    }
}