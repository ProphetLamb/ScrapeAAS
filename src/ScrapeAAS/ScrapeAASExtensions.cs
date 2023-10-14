using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

public static class ScrapeAASExtensions
{
    public static IServiceCollection AddScrapeAAS(
        this IServiceCollection services
        )
    {
        return services.AddScrapeAAS(config => config.UseDefaultConfiguration());
    }

    public static IScrapeAASConfiguration UseDefaultConfiguration(this IScrapeAASConfiguration configuration)
    {
        return configuration
            .WithLongLivingServiceLifetime(ServiceLifetime.Scoped)
            .UseInMemoryCookiesStorage()
            .UseHttpClientStaticPageLoader()
            .UsePuppeteerBrowserPageLoader()
            .UseMessagePipeDataFlow()
            .UseAngleSharpPageLoader();
    }
}
