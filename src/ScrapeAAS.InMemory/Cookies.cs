using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

internal sealed class InMemoryCookiesStorage : ICookiesStorage
{
    private CookieContainer? _cookieContainer;

    public ValueTask<CookieContainer> GetAsync(CancellationToken cancellationToken = default)
    {
        return new(_cookieContainer ??= new CookieContainer());
    }

    public ValueTask SetAsync(CookieContainer cookieCollection, CancellationToken cancellationToken = default)
    {
        _cookieContainer = cookieCollection;
        return default;
    }
}

public static class InMemoryCookiesStorageExtensions
{
    public static IServiceCollection AddInMemoryCookiesStorage(this IServiceCollection services)
    {
        return services.AddSingleton<ICookiesStorage, InMemoryCookiesStorage>();
    }
}