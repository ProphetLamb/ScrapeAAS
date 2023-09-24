using System.Net;

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
