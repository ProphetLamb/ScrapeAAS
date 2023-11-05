using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScrapeAAS;

public sealed class WebShareProviderOptions : IOptions<WebShareProviderOptions>
{
    public string? ApiKey { get; set; }
    public WebShareProxyMode Mode { get; set; }
    public IEnumerable<RegionInfo>? AllowedContries { get; set; }
    public Action<WebProxy>? ConfigureProxy { get; set; }
    public TimeSpan? CacheExpiration { get; set; }

    WebShareProviderOptions IOptions<WebShareProviderOptions>.Value => this;
}

public enum WebShareProxyMode
{
    Backbone,
    Direct
}

public interface IWebShareProxyListProvider
{
    ValueTask<IEnumerable<WebProxy>> GetProxyListAsync(CancellationToken cancellationToken = default);
}

public interface IRawWebShareProxyListProvider : IWebShareProxyListProvider { }

internal sealed class WebShareProxyListProvider(IOptions<WebShareProviderOptions> options, IHttpClientFactory httpClientFactory) : IRawWebShareProxyListProvider
{
    private readonly WebShareProviderOptions _options = options.Value;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public async ValueTask<IEnumerable<WebProxy>> GetProxyListAsync(CancellationToken cancellationToken = default)
    {
        using var client = _httpClientFactory.CreateClient();

        var reqUri = new UriBuilder("https://proxy.webshare.io/api/v2/proxy/list/")
        {
            Query = GetProxyListRequestQueryParamsAsNameValueCollection().ToString(),
        }.Uri;
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance
        };

        List<WebProxy> results = [];
        for (var pageNo = 0; pageNo < 10 && reqUri is not null; pageNo++)
        {
            HttpRequestMessage req = new(HttpMethod.Get, reqUri);
            req.Headers.Add("Authorization", $"Token {_options.ApiKey}");
            var rsp = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var envelope = await rsp.Content.ReadFromJsonAsync<ProxyListResponseEnvelope>().ConfigureAwait(false);
            results.AddRange(envelope.Results.Where(item => item.Valid).Select(ProxyListResponseToWebProxy));
        }
        return results;
    }

    private IEnumerable<KeyValuePair<string, string>> GetProxyListRequestQueryParams()
    {
        yield return new("mode", _options.Mode switch
        {
            WebShareProxyMode.Backbone => "backbone",
            _ => "direct"
        });
        yield return new("valid", "true");
        if (_options.AllowedContries?.ToImmutableArray() is { IsDefaultOrEmpty: false } countries)
        {
            yield return new("country_code__in", string.Join(",", countries.Select(country => country.Name)));
        }
    }

    private NameValueCollection GetProxyListRequestQueryParamsAsNameValueCollection()
    {
        NameValueCollection query = [];
        foreach (var (key, value) in GetProxyListRequestQueryParams())
        {
            query.Add(key, value);
        }
        return query;
    }

    private WebProxy ProxyListResponseToWebProxy(ProxyListResponseProxyInfo info)
    {
        WebProxy proxy = new(info.ProxyAddress, info.Port)
        {
            Credentials = new NetworkCredential(info.Username, info.Password),
            BypassProxyOnLocal = true,
        };

        if (_options.ConfigureProxy is not null)
        {
            _options.ConfigureProxy(proxy);
        }

        return proxy;
    }

    internal readonly record struct ProxyListResponseEnvelope(int Count, string? Next, string? Previous, ImmutableArray<ProxyListResponseProxyInfo> Results);

    internal readonly record struct ProxyListResponseProxyInfo(string Id, string Username, string Password, string ProxyAddress, int Port, bool Valid, DateTimeOffset LastVerification, string CountryCode, string CityName, DateTimeOffset CreatedAt);
}

internal sealed class CachedWebShareProxyProvider(IRawWebShareProxyListProvider proxyListProvider, IOptions<WebShareProviderOptions> options) : IWebShareProxyListProvider
{
    private readonly IRawWebShareProxyListProvider _proxyListProvider = proxyListProvider;
    private readonly WebShareProviderOptions _options = options.Value;
    private readonly object _entryTaskSwapLock = new();
    private Entry? _entry;
    private ValueTask<IEnumerable<WebProxy>>? _entryTask;

    public ValueTask<IEnumerable<WebProxy>> GetProxyListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetWebProxyListFromEntry() is { } webProxyList)
        {
            return new(webProxyList);
        }

        lock (_entryTaskSwapLock)
        {
            if (TryGetWebProxyListFromEntry() is { } webProxyListInsideLock)
            {
                return new(webProxyListInsideLock);
            }

            if (_entryTask is { } entryTask)
            {
                return entryTask;
            }

            _entryTask = entryTask = RefreshProxyListCacheEntryAsync(cancellationToken);
            return entryTask;
        }
    }

    private IEnumerable<WebProxy>? TryGetWebProxyListFromEntry()
    {
        var now = TimeSpan.FromTicks(Stopwatch.GetTimestamp());
        var cacheExpiration = _options.CacheExpiration ?? TimeSpan.FromHours(4);
        if (_entry is { } entry && entry.CreatedAt + cacheExpiration >= now)
        {
            return _entry.Proxies;
        }

        return null;
    }

    private async ValueTask<IEnumerable<WebProxy>> RefreshProxyListCacheEntryAsync(CancellationToken cancellationToken)
    {
        var proxyList = await _proxyListProvider.GetProxyListAsync(cancellationToken).ConfigureAwait(false);
        _entry = new(proxyList, TimeSpan.FromTicks(Stopwatch.GetTimestamp()));
        _entryTask = null;
        cancellationToken.ThrowIfCancellationRequested();
        return proxyList;
    }

    internal sealed record Entry(IEnumerable<WebProxy> Proxies, TimeSpan CreatedAt);
}

internal sealed class RandomWebShareProxyProvider(IWebShareProxyListProvider proxyListProvider) : IProxyProvider
{
    private readonly IWebShareProxyListProvider _proxyListProvider = proxyListProvider;

    public async ValueTask<WebProxy> GetProxyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var proxyList = await _proxyListProvider.GetProxyListAsync(cancellationToken).ConfigureAwait(false);
        return SelectRandomProxy(proxyList.ToImmutableArray());
    }

    private WebProxy SelectRandomProxy(ImmutableArray<WebProxy> proxyList)
    {
        var proxyIndex = Random.Shared.Next(proxyList.Length);
        return proxyList[proxyIndex];
    }
}

public static class WebShareProviderExtensions
{
    public static IScrapeAASConfiguration AddWebShareProxyProvider(this IScrapeAASConfiguration configuration, Action<WebShareProviderOptions> configure)
    {
        configuration.Use(ScrapeAASRole.ProxyProvider, services =>
        {
            _ = services.Configure(configure);
            _ = services
                .AddSingleton<IRawWebShareProxyListProvider, WebShareProxyListProvider>()
                .AddSingleton<IWebShareProxyListProvider, CachedWebShareProxyProvider>()
                .AddSingleton<IProxyProvider, RandomWebShareProxyProvider>();
        });
        return configuration;
    }
}
