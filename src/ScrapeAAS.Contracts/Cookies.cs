using System.Net;

namespace ScrapeAAS;

/// <summary>
/// Stores cookies.
/// </summary>
public interface ICookiesStorage
{
    /// <summary>
    /// Sets the cookies.
    /// </summary>
    /// <param name="cookieCollection">The cookies to set.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask SetAsync(CookieContainer cookieCollection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the cookies.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cookies.</returns>
    ValueTask<CookieContainer> GetAsync(CancellationToken cancellationToken = default);
}
