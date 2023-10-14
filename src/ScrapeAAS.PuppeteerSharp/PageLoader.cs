using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.RateLimit;
using PuppeteerSharp;

namespace ScrapeAAS;

public sealed class PageLoaderOptions : IOptions<PageLoaderOptions>
{
    public AsyncPolicy? RequestPolicy { get; set; } = CreateDefaultRequestPolicy();

    PageLoaderOptions IOptions<PageLoaderOptions>.Value => this;

    private static AsyncPolicy CreateDefaultRequestPolicy()
    {
        var retryWithBackoff = Policy.Handle<Exception>(ex => ex is PuppeteerException or HttpRequestException or RateLimitRejectedException)
            .WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromMinutes(2), 10))
            .WrapAsync(Policy.RateLimitAsync(20, TimeSpan.FromMinutes(1)));
        var rateLimiter = Policy.Handle<RateLimitRejectedException>()
            .WaitAndRetryForeverAsync(_ => TimeSpan.FromSeconds(5))
            .WrapAsync(Policy.RateLimitAsync(1, TimeSpan.FromSeconds(4)));
        return retryWithBackoff.WrapAsync(rateLimiter);
    }
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
    public static IScrapeAASConfiguration UsePuppeteerBrowserPageLoader(this IScrapeAASConfiguration configuration, Action<PageLoaderOptions>? pageLoaderOptionsConfiguration = null, Action<PuppeteerBrowserOptions>? puppeteerBrowserConfiguration = null, Action<PuppeteerPageHandlerFactoryOptions>? puppeteerPageHandlerFactoryConfiguration = null)
    {
        configuration.Use(ScrapeAASRole.BrowserPageLoader, (configuration, services) =>
        {
            ConfigureOptions();
            // Browser infrastructure
            _ = services.AddSingleton<IPuppeteerInstallationProvider, PuppeteerInstallationProvider>();
            _ = services.AddSingleton<IPuppeteerBrowserProvider, PuppeteerBrowserProvider>();
            // Page handler factory
            _ = services.AddTransient<IPuppeteerPageHandlerBuilder, LazyInitializationPageHandlerBuilder>();
            _ = services.AddSingleton<IPuppeteerPageHandlerFactory, DefaultPuppeteerPageHandlerFactory>();
            // Transient page loaders
            _ = services.AddTransient(static sp => sp.GetRequiredService<IPuppeteerPageHandlerFactory>().CreateHandler(new()));
            _ = services.AddTransient<IRawBrowserPageLoader, RawPuppeteerBrowserPageLoader>();
            _ = services.AddTransient<IBrowserPageLoader, PollyPuppeteerBrowserPageLoader>();

            void ConfigureOptions()
            {
                var pageLoaderOptions = services.AddOptions<PageLoaderOptions>();
                if (pageLoaderOptionsConfiguration is not null)
                {
                    _ = pageLoaderOptions.Configure(pageLoaderOptionsConfiguration);
                }
                var puppeteerBrowserOptions = services.AddOptions<PuppeteerBrowserOptions>();
                if (puppeteerBrowserConfiguration is not null)
                {
                    _ = puppeteerBrowserOptions.Configure(puppeteerBrowserConfiguration);
                }
                var puppeteerPageHandlerFactoryOptions = services.AddOptions<PuppeteerPageHandlerFactoryOptions>();
                _ = puppeteerPageHandlerFactoryOptions.Configure(options => options.SuppressServiceScope = configuration.LongLivingServiceLifetime == ServiceLifetime.Singleton);
                if (puppeteerPageHandlerFactoryConfiguration is not null)
                {
                    _ = puppeteerPageHandlerFactoryOptions.Configure(puppeteerPageHandlerFactoryConfiguration);
                }
            }

        });
        return configuration;
    }
}

public static class StaticPageLoaderExtensions
{
    public static IScrapeAASConfiguration UseHttpClientStaticPageLoader(this IScrapeAASConfiguration configuration)
    {
        configuration.Use(ScrapeAASRole.StaticPageLoader, (configuration, services) =>
        {
            _ = services.AddHttpClient<IRawStaticPageLoader>()
                .ConfigureHttpClient(ConfigureHttpClient)
                .ConfigureHttpMessageHandlerBuilder(ConfigureMessageHandler);

            _ = services.AddTransient<IRawStaticPageLoader, RawHttpClientStaticPageLoader>();
            _ = services.AddTransient<IStaticPageLoader, PollyHttpClientStaticPageLoader>();

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
                var proxyProvider = builder.Services.GetService<IProxyProvider>();
                var cookiesStorage = builder.Services.GetService<ICookiesStorage>();
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
        });

        return configuration;
    }
}
