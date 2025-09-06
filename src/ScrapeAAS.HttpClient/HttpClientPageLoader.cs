using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace ScrapeAAS;

/// <remarks>
/// Implemented for internal use. No retry policy is applied.
/// </remarks>
public interface IRawStaticPageLoader : IStaticPageLoader;

internal sealed class RawHttpClientStaticPageLoader(ILogger<PollyHttpClientStaticPageLoader> logger, HttpClient httpClient) : IRawStaticPageLoader
{
    public async Task<HttpContent> LoadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Loading page {Url}", url);

        using HttpRequestMessage req = new(HttpMethod.Get, url);
        var rsp = await httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
        _ = rsp.EnsureSuccessStatusCode();

        logger.LogDebug("Page {Url} loaded", url);
        return rsp.Content;
    }
}


internal sealed class PollyHttpClientStaticPageLoader(ILogger<PollyHttpClientStaticPageLoader> logger, IOptions<PageLoaderOptions> options, IRawStaticPageLoader rawStaticPageLoader) : IStaticPageLoader
{
    public async Task<HttpContent> LoadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var policy = options.Value.RequestPolicy ?? Policy.NoOpAsync();
        var content = await policy.ExecuteAsync(cancellationToken => rawStaticPageLoader.LoadAsync(url, cancellationToken), cancellationToken, continueOnCapturedContext: false).ConfigureAwait(false);
        return content;
    }
}
