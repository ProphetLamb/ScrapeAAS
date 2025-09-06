using System.Collections.Immutable;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ScrapeAAS.Tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void TestInjectBrowserPage()
    {
        var builder = Host.CreateApplicationBuilder();
        _ = builder.Services
            .AddScrapeAAS(config => config
                .WithLongLivingServiceLifetime(ServiceLifetime.Scoped)
                .UseHttpClientStaticPageLoader()
                .UsePuppeteerBrowserPageLoader()
                .UseInMemoryCookiesStorage()
                .UseMessagePipeDataflow()
                .AddDataflow<BrowserPageLoadHandler>()
            )
            .AddHostedService<PupeeteerBrowserPageLoaderService>();
        var app = builder.Build();
        app.Run();
    }
}

internal sealed class PupeeteerBrowserPageLoaderService(IServiceScopeFactory services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IAsyncMessageHandler<BrowserPageLoadParameter>>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDataflowPublisher<BrowserPageLoadParameter>>();
        await publisher.PublishAsync(new(new("https://www.google.com"), ImmutableArray<PageAction>.Empty, true), stoppingToken).ConfigureAwait(false);
        Environment.Exit(0);
    }
}

internal sealed class BrowserPageLoadHandler(ILogger<BrowserPageLoadHandler> logger) : IDataflowHandler<BrowserPageLoadParameter>
{
    public async ValueTask HandleAsync(BrowserPageLoadParameter message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Url: {Url}", message.Url);
        await Task.Delay(10).ConfigureAwait(false);
    }
}
