using System;
using System.Net;

namespace ScrapeAAS.Contracts;

public interface IProxyProvider
{
    /// <summary>
    /// Gets a proxy
    /// </summary>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task that completes when the proxy has been retrieved</returns>
    ValueTask<WebProxy> GetProxyAsync(CancellationToken cancellationToken = default);
}
