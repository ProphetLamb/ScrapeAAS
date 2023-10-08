using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Polly;
using PuppeteerSharp;

namespace ScrapeAAS;

public sealed class PageLoaderOptions : IOptions<PageLoaderOptions>
{
    public AsyncPolicy? RequestPolicy { get; set; }

    PageLoaderOptions IOptions<PageLoaderOptions>.Value => this;
}

public static class PuppeteerBrowserExtensions
{
    public static Task ApplyAsync(this PageAction pageAction, IPage page, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return pageAction.Type switch
        {
            PageActionType.Click when pageAction.TryGetClick(out var selector) => page.ClickAsync(selector, new() { Delay = Random.Shared.Next(50, 400) }),
            PageActionType.Wait when pageAction.TryGetWait(out var milliseconds) => Task.Delay(milliseconds, cancellationToken),
            PageActionType.ScrollToEnd => page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight);"),
            PageActionType.EvaluateExpression when pageAction.TryGetEvaluateExpression(out var expression) => page.EvaluateExpressionAsync(expression),
            PageActionType.EvaluateFunction when pageAction.TryGetEvaluateFunction(out var pageFunction, out var parameters) => page.EvaluateFunctionAsync(pageFunction, parameters),
            PageActionType.WaitForSelector when pageAction.TryGetWaitForSelector(out var selector) => page.WaitForSelectorAsync(selector),
            PageActionType.WaitForNetworkIdle => page.WaitForNetworkIdleAsync(),
            _ => throw new NotImplementedException("Unknown page action type"),
        };
    }

    public static CookieParam ToPuppeteerCookie(this Cookie cookie)
    {
        return new CookieParam()
        {
            Name = cookie.Name,
            Value = cookie.Value,
            Domain = cookie.Domain,
            Path = cookie.Path,
            Expires = ((DateTimeOffset)cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds(),
            HttpOnly = cookie.HttpOnly,
            Secure = cookie.Secure
        };
    }

    /// <summary>
    /// Adds the puppeteer <see cref="IBrowserPageLoader"/> to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="puppeteerBrowserConfiguration">The puppeteer browser configuration.</param>
    /// <param name="puppeteerPageHandlerFactoryConfiguration">The page handler factory configuration.</param>
    public static IServiceCollection AddPuppeteerBrowserPageLoader(this IServiceCollection services, Action<PageLoaderOptions>? pageLoaderOptionsConfiguration = null, Action<PuppeteerBrowserOptions>? puppeteerBrowserConfiguration = null, Action<PuppeteerPageHandlerFactoryOptions>? puppeteerPageHandlerFactoryConfiguration = null)
    {
        ConfigureOptions();
        // Browser infrastructure
        services.AddSingleton<IPuppeteerInstallationProvider, PuppeteerInstallationProvider>();
        services.AddSingleton<IPuppeteerBrowserProvider, PuppeteerBrowserProvider>();
        // Page handler factory
        services.AddTransient<IPuppeteerPageHandlerBuilder, LazyInitializationPageHandlerBuilder>();
        services.AddSingleton<IPuppeteerPageHandlerFactory, DefaultPuppeteerPageHandlerFactory>();
        // Transient page loaders
        services.AddTransient(static sp => sp.GetRequiredService<IPuppeteerPageHandlerFactory>().CreateHandler(new()));
        services.AddTransient<IRawBrowserPageLoader, RawPuppeteerBrowserPageLoader>();
        services.AddTransient<IBrowserPageLoader, PollyPuppeteerBrowserPageLoader>();
        return services;

        void ConfigureOptions()
        {
            var pageLoaderOptions = services.AddOptions<PageLoaderOptions>();
            if (pageLoaderOptionsConfiguration is not null)
            {
                pageLoaderOptions.Configure(pageLoaderOptionsConfiguration);
            }
            var puppeteerBrowserOptions = services.AddOptions<PuppeteerBrowserOptions>();
            if (puppeteerBrowserConfiguration is not null)
            {
                puppeteerBrowserOptions.Configure(puppeteerBrowserConfiguration);
            }
            var puppeteerPageHandlerFactoryOptions = services.AddOptions<PuppeteerPageHandlerFactoryOptions>();
            if (puppeteerPageHandlerFactoryConfiguration is not null)
            {
                puppeteerPageHandlerFactoryOptions.Configure(puppeteerPageHandlerFactoryConfiguration);
            }
        }
    }
}

public static class StaticPageLoaderExtensions
{
    public static IServiceCollection AddHttpClientStaticPageLoader(this IServiceCollection services)
    {
        services.AddHttpClient<IRawStaticPageLoader>()
            .ConfigureHttpClient(ConfigureHttpClient)
            .ConfigureHttpMessageHandlerBuilder(ConfigureMessageHandler);

        services.AddTransient<IRawStaticPageLoader, RawHttpClientStaticPageLoader>();
        services.AddTransient<IStaticPageLoader, PollyHttpClientStaticPageLoader>();
        return services;

        static void ConfigureHttpClient(HttpClient client)
        {
            AddDefaultRequestHeaders(client.DefaultRequestHeaders);
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.Timeout = TimeSpan.FromSeconds(30);
        }

        static void AddDefaultRequestHeaders(HttpRequestHeaders headers)
        {
            headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
            headers.Add("Accept-Encoding", "gzip, deflate, br");
            headers.Add("Accept-Language", "en-US,en;q=0.5");
            headers.Add("Sec-Fetch-Dest", "document");
            headers.Add("Sec-Fetch-Mode", "navigate");
            headers.Add("Sec-Fetch-Site", "none");
            headers.Add("Sec-Fetch-User", "?1");
            headers.Add("Upgrade-Insecure-Requests", "1");
            headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36");
        }

        static void ConfigureMessageHandler(HttpMessageHandlerBuilder builder)
        {
            IProxyProvider? proxyProvider = builder.Services.GetService<IProxyProvider>();
            ICookiesStorage? cookiesStorage = builder.Services.GetService<ICookiesStorage>();
            HttpClientHandler handler = new();
            if (proxyProvider is not null)
            {
                handler.Proxy = proxyProvider.GetProxyAsync().GetAwaiter().GetResult();
            }
            if (cookiesStorage is not null)
            {
                handler.CookieContainer = cookiesStorage.GetAsync().GetAwaiter().GetResult();
            }
            builder.PrimaryHandler = handler;
        }
    }
}
