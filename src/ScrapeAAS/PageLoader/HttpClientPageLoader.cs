using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using ScrapeAAS.Contracts;
using ScrapeAAS.Extensions;

namespace ScrapeAAS.PageLoader;

/// <remarks>
/// Implemented for internal use. No retry policy is applied.
/// </remarks>
public interface IRawStaticPageLoader : IStaticPageLoader { }

internal sealed class RawHttpClientStaticPageLoader : IRawStaticPageLoader
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly PageLoaderOptions _options;

    public RawHttpClientStaticPageLoader(ILogger<PollyHttpClientStaticPageLoader> logger, HttpClient httpClient, IOptions<PageLoaderOptions> options)
    {
        _logger = logger;
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<HttpContent> LoadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        using var _ = _logger.LogMethodDuration();

        _logger.LogDebug("Loading page {Url}", url);

        HttpRequestMessage req = new(HttpMethod.Get, url);
        var rsp = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
        rsp.EnsureSuccessStatusCode();

        _logger.LogDebug("Page {Url} loaded", url);
        return rsp.Content;
    }
}


internal sealed class PollyHttpClientStaticPageLoader : IStaticPageLoader
{
    private readonly ILogger _logger;
    private readonly PageLoaderOptions _options;
    private readonly IRawStaticPageLoader _rawStaticPageLoader;

    public PollyHttpClientStaticPageLoader(ILogger<PollyHttpClientStaticPageLoader> logger, IOptions<PageLoaderOptions> options, IRawStaticPageLoader rawStaticPageLoader)
    {
        _logger = logger;
        _options = options.Value;
        _rawStaticPageLoader = rawStaticPageLoader;
    }

    public async Task<HttpContent> LoadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        using var _ = _logger.LogMethodDuration();
        var policy = _options.RequestPolicy ?? Policy.NoOpAsync();
        var content = await policy.ExecuteAsync(cancellationToken => _rawStaticPageLoader.LoadAsync(url, cancellationToken), cancellationToken, continueOnCapturedContext: false).ConfigureAwait(false);
        return content;
    }
}
