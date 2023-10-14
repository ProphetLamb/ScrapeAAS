using System;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.RateLimit;

namespace ScrapeAAS;

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
