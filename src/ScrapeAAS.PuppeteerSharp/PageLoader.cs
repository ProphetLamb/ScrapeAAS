using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
