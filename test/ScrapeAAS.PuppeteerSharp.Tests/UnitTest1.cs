using System.Net.Mime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScrapeAAS;

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
            .AddPuppeteerBrowserPageLoader()
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
        using var scope = _services.CreateScope();
        var page = scope.ServiceProvider.GetRequiredService<IBrowserPageLoader>();
        var content = await page.LoadAsync(new Uri("https://www.google.com/"));
        var html = await content.ReadAsStringAsync();

        Environment.Exit(0);
    }
}