using System;
using System.Net.Http;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.RateLimit;

namespace ScrapeAAS;

public sealed class PageLoaderOptions : IOptions<PageLoaderOptions>
{
    public AsyncPolicy? RequestPolicy { get; set; } = CreateDefaultRequestPolicy();

    PageLoaderOptions IOptions<PageLoaderOptions>.Value => this;

    private static AsyncPolicy CreateDefaultRequestPolicy()
    {
        var retryWithBackoff = Policy.Handle<Exception>()
            .WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromMinutes(2), 10))
            .WrapAsync(Policy.RateLimitAsync(20, TimeSpan.FromMinutes(1)));
        var rateLimiter = Policy.Handle<RateLimitRejectedException>()
            .WaitAndRetryForeverAsync(_ => TimeSpan.FromSeconds(5))
            .WrapAsync(Policy.RateLimitAsync(1, TimeSpan.FromSeconds(4)));
        return retryWithBackoff.WrapAsync(rateLimiter);
    }
}