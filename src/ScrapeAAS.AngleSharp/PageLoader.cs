﻿using AngleSharp;
using Microsoft.Extensions.Options;
using AngleSharp.Dom;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

public interface IAngleSharpStaticPageLoader
{
    Task<IDocument> LoadAsync(Uri url, CancellationToken cancellationToken = default);
}

public sealed class AngleSharpPageLoaderOptions : IOptions<AngleSharpPageLoaderOptions>
{
    public IConfiguration? Configuration { get; set; }

    AngleSharpPageLoaderOptions IOptions<AngleSharpPageLoaderOptions>.Value => this;
}

internal sealed class AngleSharpStaticPageLoader : IAngleSharpStaticPageLoader
{
    private readonly IStaticPageLoader _pageLoader;
    private readonly AngleSharpPageLoaderOptions _options;
    private readonly IBrowsingContext _context;

    public AngleSharpStaticPageLoader(IStaticPageLoader pageLoader, IOptions<AngleSharpPageLoaderOptions> options)
    {
        _pageLoader = pageLoader;
        _options = options.Value;

        var config = _options.Configuration ?? Configuration.Default.WithDefaultLoader();
        _context = BrowsingContext.New(config);
    }

    public async Task<IDocument> LoadAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var content = await _pageLoader.LoadAsync(url, cancellationToken);
        var contentStream = await content.ReadAsStreamAsync();
        return await _context.OpenAsync(req => req.Content(contentStream), cancellationToken);
    }
}

public interface IAngleSharpBrowserPageLoader
{
    Task<IDocument> LoadAsync(BrowserPageLoadParameter parameter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<IDocument> LoadAsync(Uri url, CancellationToken cancellationToken = default) => LoadAsync(new BrowserPageLoadParameter(url, default, true), cancellationToken);
    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="headless">Whether to run the browser in headless mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<IDocument> LoadAsync(Uri url, bool headless, CancellationToken cancellationToken = default) => LoadAsync(new BrowserPageLoadParameter(url, default, headless), cancellationToken);
    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="pageActions">The actions to perform when the page is loaded.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<IDocument> LoadAsync(Uri url, params PageAction[] pageActions) => LoadAsync(new BrowserPageLoadParameter(url, pageActions.ToImmutableArray(), true));
    /// <summary>
    /// Loads a page in a browser.
    /// </summary>
    /// <param name="url">The url to load.</param>
    /// <param name="pageActions">The actions to perform when the page is loaded.</param>
    /// <param name="headless">Whether to run the browser in headless mode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The content of the loaded page.</returns>
    Task<IDocument> LoadAsync(Uri url, ImmutableArray<PageAction> pageActions, bool headless, CancellationToken cancellationToken = default) => LoadAsync(new BrowserPageLoadParameter(url, pageActions, headless), cancellationToken);

}

internal sealed class AngleSharpBrowserPageLoader : IAngleSharpBrowserPageLoader
{
    private readonly IBrowserPageLoader _pageLoader;
    private readonly AngleSharpPageLoaderOptions _options;
    private IBrowsingContext _context;

    public AngleSharpBrowserPageLoader(IBrowserPageLoader pageLoader, IOptions<AngleSharpPageLoaderOptions> options)
    {
        _pageLoader = pageLoader;
        _options = options.Value;

        var config = _options.Configuration ?? Configuration.Default.WithDefaultLoader();
        _context = BrowsingContext.New(config);
    }

    public async Task<IDocument> LoadAsync(BrowserPageLoadParameter parameter, CancellationToken cancellationToken = default)
    {
        var content = await _pageLoader.LoadAsync(parameter, cancellationToken);
        var contentStream = await content.ReadAsStreamAsync();
        return await _context.OpenAsync(req => req.Content(contentStream), cancellationToken);
    }
}

public static class AgnleSharpPageLoaderExtensions
{
    public static IServiceCollection AddAngleSharpPageLoader(this IServiceCollection services, Action<AngleSharpPageLoaderOptions>? angleSharpPageLoaderConfiguration = null)
    {
        var angleSharpPageLoaderOption = services.AddOptions<AngleSharpPageLoaderOptions>();
        if (angleSharpPageLoaderConfiguration is not null)
        {
            angleSharpPageLoaderOption.Configure(angleSharpPageLoaderConfiguration);
        }

        services.AddTransient<IAngleSharpStaticPageLoader, AngleSharpStaticPageLoader>();
        services.AddTransient<IAngleSharpBrowserPageLoader, AngleSharpBrowserPageLoader>();

        return services;
    }
}