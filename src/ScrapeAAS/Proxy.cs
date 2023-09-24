using System.Net;
using Microsoft.Extensions.Options;
using ScrapeAAS.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

public sealed class SingleProxyProviderOptions : IOptions<SingleProxyProviderOptions>
{
    public WebProxy Proxy { get; set; } = new();

    SingleProxyProviderOptions IOptions<SingleProxyProviderOptions>.Value => this;
}

internal class SingleProxyProvider : IProxyProvider
{
    private readonly SingleProxyProviderOptions _options;

    public SingleProxyProvider(IOptions<SingleProxyProviderOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<WebProxy> GetProxyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(_options.Proxy);
    }
}

public static class ProxyExtensions
{
    public static IServiceCollection AddSingleProxyProvider(this IServiceCollection services, Action<SingleProxyProviderOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IProxyProvider, SingleProxyProvider>();
        return services;
    }
}