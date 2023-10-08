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
        builder.Services
            .AddInMemoryCookiesStorage()
            .AddMessagePipeDataFlow(options => options.RequestHandlerLifetime = InstanceLifetime.Scoped)
            .AddDataFlow<BrowserPageLoadHandler>()
            .AddHostedService<PupeeteerBrowserPageLoaderService>();
        var app = builder.Build();
        app.Run();
    }
}

sealed class PupeeteerBrowserPageLoaderService : BackgroundService
{
    private readonly ILogger<PupeeteerBrowserPageLoaderService> _logger;
    private readonly IServiceScopeFactory _services;

    public PupeeteerBrowserPageLoaderService(ILogger<PupeeteerBrowserPageLoaderService> logger, IServiceScopeFactory services)
    {
        _logger = logger;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IAsyncMessageHandler<BrowserPageLoadParameter>>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDataflowPublisher<BrowserPageLoadParameter>>();
        await publisher.PublishAsync(new(new("https://www.google.com"), ImmutableArray<PageAction>.Empty, true), stoppingToken);
        Environment.Exit(0);
    }
}

sealed class BrowserPageLoadHandler : IDataflowHandler<BrowserPageLoadParameter>
{
    private readonly ILogger<BrowserPageLoadHandler> _logger;

    public BrowserPageLoadHandler(ILogger<BrowserPageLoadHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask HandleAsync(BrowserPageLoadParameter message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Url: {Url}", message.Url);
    }
}
