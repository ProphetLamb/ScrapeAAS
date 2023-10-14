using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace ScrapeAAS;

/// <remarks>
/// Implemented for internal use. No retry policy is applied.
/// </remarks>
public interface IRawStaticPageLoader : IStaticPageLoader { }

internal sealed class RawHttpClientStaticPageLoader(ILogger<PollyHttpClientStaticPageLoader> logger, HttpClient httpClient) : IRawStaticPageLoader
{
    private readonly ILogger _logger = logger;
    private readonly HttpClient _httpClient = httpClient;
    public async Task<HttpContent> LoadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading page {Url}", url);

        HttpRequestMessage req = new(HttpMethod.Get, url);
        var rsp = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _ = rsp.EnsureSuccessStatusCode();

        _logger.LogDebug("Page {Url} loaded", url);
        return rsp.Content;
    }
}


internal sealed class PollyHttpClientStaticPageLoader(ILogger<PollyHttpClientStaticPageLoader> logger, IOptions<PageLoaderOptions> options, IRawStaticPageLoader rawStaticPageLoader) : IStaticPageLoader
{
    private readonly ILogger _logger = logger;
    private readonly PageLoaderOptions _options = options.Value;
    private readonly IRawStaticPageLoader _rawStaticPageLoader = rawStaticPageLoader;

    public async Task<HttpContent> LoadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var policy = _options.RequestPolicy ?? Policy.NoOpAsync();
        var content = await policy.ExecuteAsync(cancellationToken => _rawStaticPageLoader.LoadAsync(url, cancellationToken), cancellationToken, continueOnCapturedContext: false).ConfigureAwait(false);
        return content;
    }
}
