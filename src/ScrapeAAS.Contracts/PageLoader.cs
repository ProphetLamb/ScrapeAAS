using System.Threading;
using System.Collections.Immutable;

namespace ScrapeAAS.Contracts;

/// <summary>
/// Loads a static page.
/// </summary>
public interface IStaticPageLoader
{
    /// <summary>
    /// Loads the content of a static page.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<HttpContent> LoadAsync(Uri url, CancellationToken cancellationToken = default);
}

/// <summary>
/// The parameter on how to load a browser page.
/// </summary>
/// <seealso cref="IBrowserPageLoader"/>
public readonly struct BrowserPageLoadParameter
{
    public BrowserPageLoadParameter(Uri url, ImmutableArray<PageAction> pageActions, bool headless)
    {
        Url = url;
        PageActions = pageActions;
        Headless = headless;
    }

    /// <summary>
    /// The url to load.
    /// </summary>
    public Uri Url { get; }
    /// <summary>
    /// The actions to perform when the page is loaded.
    /// </summary>
    public ImmutableArray<PageAction> PageActions { get; }
    /// <summary>
    /// Whether to run the browser in headless mode.
    /// </summary>
    public bool Headless { get; }
}

/// <summary>
/// Loads a page in a browser.
/// </summary>
public interface IBrowserPageLoader
{
    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="parameter">The parameter on how to load the page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<HttpContent> LoadAsync(BrowserPageLoadParameter parameter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<HttpContent> LoadAsync(Uri url, CancellationToken cancellationToken = default) => LoadAsync(new BrowserPageLoadParameter(url, default, true), cancellationToken);
    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="headless">Whether to run the browser in headless mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<HttpContent> LoadAsync(Uri url, bool headless, CancellationToken cancellationToken = default) => LoadAsync(new BrowserPageLoadParameter(url, default, headless), cancellationToken);
    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="pageActions">The actions to perform when the page is loaded.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<HttpContent> LoadAsync(Uri url, params PageAction[] pageActions) => LoadAsync(new BrowserPageLoadParameter(url, pageActions.ToImmutableArray(), true));
    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="pageActions">The actions to perform when the page is loaded.</param>
    /// <param name="headless">Whether to run the browser in headless mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<HttpContent> LoadAsync(Uri url, ImmutableArray<PageAction> pageActions, bool headless, CancellationToken cancellationToken = default) => LoadAsync(new BrowserPageLoadParameter(url, pageActions, headless), cancellationToken);
}
