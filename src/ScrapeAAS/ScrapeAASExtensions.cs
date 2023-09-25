using System;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

public sealed class ScrapeAASConfiguration
{
    public Action<AngleSharpPageLoaderOptions>? AngleSharp { get; set; } = null;
    public Action<PuppeteerBrowserOptions>? PuppeteerBrowser { get; set; } = null;
    public Action<PuppeteerPageHandlerFactoryOptions>? PuppeteerPageHandlerFactory { get; set; } = null;
    public Action<MessagePipeOptions>? MessagePipe { get; set; } = null;
}

public static class ScrapeAASExtensions
{
    public static IServiceCollection AddScrapeAAS(
        this IServiceCollection services,
        ScrapeAASConfiguration? configuration = null
        )
    {
        return services
            .AddHttpClientStaticPageLoader()
            .AddPuppeteerBrowserPageLoader(configuration?.PuppeteerBrowser, configuration?.PuppeteerPageHandlerFactory)
            .AddAngleSharpPageLoader(configuration?.AngleSharp)
            .AddMessagePipeDataFlow(configuration?.MessagePipe);
    }
}
