using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
                .UseInMemoryCookiesStorage()
                .UsePuppeteerBrowserPageLoader()
            )
            .AddHostedService<PupeeteerBrowserPageLoaderService>();
        var app = builder.Build();
        app.Run();
    }
}

internal sealed class PupeeteerBrowserPageLoaderService(IServiceScopeFactory services) : BackgroundService
{
    private readonly IServiceScopeFactory _services = services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var page = scope.ServiceProvider.GetRequiredService<IBrowserPageLoader>();
        var content = await page.LoadAsync(new Uri("https://www.google.com/"));
        var html = await content.ReadAsStringAsync();

        Environment.Exit(0);
    }
}