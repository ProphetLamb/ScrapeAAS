using Microsoft.Extensions.Logging;
using ScrapeAAS.Contracts;
using ScrapeAAS.Extensions;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using System.Collections.Concurrent;
using System.Diagnostics;
using ScrapeAAS.Utility;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using ScrapeAAS.Contracts;
using Polly;

namespace ScrapeAAS.PageLoader;

public sealed class PuppeteerBrowserOptions : IOptions<PuppeteerBrowserOptions>
{
    public string? ExecutablePath { get; set; }

    public TimeSpan? BrowserSlidingExpiration { get; set; }

    PuppeteerBrowserOptions IOptions<PuppeteerBrowserOptions>.Value => this;
}

/// <remarks>
/// Implemented for internal use. No retry policy is applied.
/// </remarks>
public interface IRawBrowserPageLoader : IBrowserPageLoader { }

public interface IPuppeteerInstallationProvider
{
    ValueTask<InstalledBrowser> GetBrowser(SupportedBrowser browser, CancellationToken cancellationToken = default);
}

internal sealed class PuppeteerInstallationProvider : IPuppeteerInstallationProvider
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _browserFetcherMutex = new(1, 1);
    private readonly PageLoaderOptions _options;
    private readonly PuppeteerBrowserOptions _puppeteerBrowserOptions;
    private readonly ConcurrentDictionary<SupportedBrowser, InstalledBrowser> _installedBrowsers = new();

    public PuppeteerInstallationProvider(ILogger<RawPuppeteerBrowserPageLoader> logger, IOptions<PageLoaderOptions> options, IOptions<PuppeteerBrowserOptions> browserOptions)
    {
        _logger = logger;
        _options = options.Value;
        _puppeteerBrowserOptions = browserOptions.Value;
    }

    public ValueTask<InstalledBrowser> GetBrowser(SupportedBrowser browser, CancellationToken cancellationToken = default)
    {
        if (_installedBrowsers.TryGetValue(browser, out var installedBrowser))
        {
            return new(installedBrowser);
        }
        return new(GetOrCreateInstalledBrowser(browser, cancellationToken));
    }

    private async Task<InstalledBrowser> GetOrCreateInstalledBrowser(SupportedBrowser browser, CancellationToken cancellationToken)
    {
        await _browserFetcherMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var _ = _logger.LogMethodDuration();
            _logger.LogDebug("Ensuring Puppeteer browser executable is installed");

            BrowserFetcher browserFetcher = new(new BrowserFetcherOptions()
            {
                Browser = SupportedBrowser.Chrome,
                Path = _puppeteerBrowserOptions.ExecutablePath,
            });

            if (browserFetcher.GetInstalledBrowsers().FirstOrDefault(browser => browser.Browser == SupportedBrowser.Chrome) is { } installedBrowser)
            {
                _logger.LogDebug("Puppeteer browser executable is already installed");
                return installedBrowser;
            }

            _logger.LogInformation("Puppeteer browser executable is not installed, downloading");

            installedBrowser = await browserFetcher.DownloadAsync().ConfigureAwait(false);

            _logger.LogInformation("Puppeteer browser executable is downloaded");
            _installedBrowsers[browser] = installedBrowser;
            return installedBrowser;
        }
        finally
        {
            _browserFetcherMutex.Release();
        }
    }
}

public interface IPuppeteerBrowserProvider
{
    ValueTask<IBrowser> GetBrowser(PuppeteerBrowserSpecificaiton browserSpecification, CancellationToken cancellationToken = default);
}

public sealed record PuppeteerBrowserSpecificaiton(SupportedBrowser SupportedBrowser = SupportedBrowser.Chrome, bool Headless = true);

internal sealed class PuppeteerBrowserProvider : IPuppeteerBrowserProvider, IAsyncDisposable
{
    private readonly IPuppeteerInstallationProvider _puppeteerBrowsers;
    private readonly ILogger _logger;
    private readonly IProxyProvider? _proxyProvider;
    private readonly SemaphoreSlim _browserInitializeMutex = new(1, 1);
    private readonly Dictionary<PuppeteerBrowserSpecificaiton, IBrowser> _browsers = new();

    public PuppeteerBrowserProvider(IPuppeteerInstallationProvider puppeteerBrowsers, ILogger<RawPuppeteerBrowserPageLoader> logger, IProxyProvider? proxyProvider = null)
    {
        _puppeteerBrowsers = puppeteerBrowsers;
        _logger = logger;
        _proxyProvider = proxyProvider;
    }

    public ValueTask DisposeAsync()
    {
        var activeDisposeTasks = _browsers.Values
            .Select(browser => browser.DisposeAsync())
            .Where(disposeTask => !disposeTask.IsCompletedSuccessfully)
            .Select(disposeTask => disposeTask.AsTask())
            .ToArray();
        _browsers.Clear();
        if (activeDisposeTasks.Length > 0)
        {
            return new(Task.WhenAll(activeDisposeTasks));
        }
        return default;
    }

    public ValueTask<IBrowser> GetBrowser(PuppeteerBrowserSpecificaiton parameter, CancellationToken cancellationToken = default)
    {
        if (_browsers.GetValueOrDefault(parameter) is { } browser)
        {
            return new(browser);
        }
        return new(CreateBrowser(parameter, cancellationToken));
    }

    private async Task<IBrowser> CreateBrowser(PuppeteerBrowserSpecificaiton parameter, CancellationToken cancellationToken)
    {
        await _browserInitializeMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_browsers.GetValueOrDefault(parameter) is { } browser)
            {
                return browser;
            }
            using var _ = _logger.LogMethodDuration();
            cancellationToken.ThrowIfCancellationRequested();
            LaunchOptions launchOptions = await CreateLaunchOptionsAsync(parameter, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var newBrowser = await Puppeteer.LaunchAsync(launchOptions).ConfigureAwait(false);

            if (_browsers.TryAdd(parameter, newBrowser))
            {
                return newBrowser;
            }
            else
            {
                await newBrowser.DisposeAsync().ConfigureAwait(false);
                return _browsers[parameter];
            }
        }
        finally
        {
            _browserInitializeMutex.Release();
        }
    }

    private async Task<LaunchOptions> CreateLaunchOptionsAsync(PuppeteerBrowserSpecificaiton parameter, CancellationToken cancellationToken = default)
    {
        var installedBrowser = await _puppeteerBrowsers.GetBrowser(parameter.SupportedBrowser, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var proxy = _proxyProvider is null ? null : (await _proxyProvider.GetProxyAsync(cancellationToken));
        return new LaunchOptions()
        {
            ExecutablePath = installedBrowser.GetExecutablePath(),
            Headless = parameter.Headless,
            Args = GetArguments().ToArray(),
        };

        IEnumerable<string> GetArguments()
        {
            yield return "--no-sandbox";
            yield return "--disable-setuid-sandbox";
            yield return "--disable-dev-shm-usage";
            if (proxy is { Address: not null })
            {
                yield return $"--proxy-server={proxy.Address.Host}:{proxy.Address.Port}";
            }
        }
    }
}

public interface IPuppeteerPageHandlerFactory
{
    IPuppeteerPageHandler CreateHandler(PuppeteerBrowserSpecificaiton browserSpecification);
}

public interface IPuppeteerPageHandler
{
    PuppeteerBrowserSpecificaiton BrowserSpecificaiton { get; }
    ValueTask<IPage> GetPageAsync(CancellationToken cancellationToken = default);
}

internal sealed class LifetimeTrackingPageHandler : IPuppeteerPageHandler
{
    private IPuppeteerPageHandler? _innerHandler;
    private volatile bool _operationStarted;
    private volatile bool _disposed;

    [DisallowNull]
    public IPuppeteerPageHandler? InnerHandler
    {
        get
        {
            return _innerHandler;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CheckDisposedOrStarted();
            _innerHandler = value;
        }
    }

    public PuppeteerBrowserSpecificaiton BrowserSpecificaiton
    {
        get
        {
            SetOperationStarted();
            return _innerHandler.BrowserSpecificaiton;
        }
    }

    public LifetimeTrackingPageHandler()
    {
    }

    public LifetimeTrackingPageHandler(IPuppeteerPageHandler innerHandler)
    {
        InnerHandler = innerHandler;
    }

    public ValueTask<IPage> GetPageAsync(CancellationToken cancellationToken = default)
    {
        SetOperationStarted();
        return _innerHandler.GetPageAsync(cancellationToken);
    }

    private void CheckDisposedOrStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_operationStarted)
        {
            throw new InvalidOperationException("The handler has already started one or more requests. Properties can only be modified before sending the first request.");
        }
    }

    [MemberNotNull(nameof(_innerHandler))]
    private void SetOperationStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_innerHandler is null)
        {
            throw new InvalidOperationException("The InnerHandler property must be initialized before use.");
        }
        // This method flags the handler instances as "active". I.e. we executed at least one request (or are
        // in the process of doing so). This information is used to lock-down all property setters. Once a
        // Send/SendAsync operation started, no property can be changed.
        if (!_operationStarted)
        {
            _operationStarted = true;
        }
    }
}

public interface IPuppeteerPageHandlerBuilder
{
    PuppeteerBrowserSpecificaiton BrowserSpecification { get; set; }
    IPuppeteerPageHandler Build();
}

internal sealed class LazyInitializationPageHandlerBuilder : IPuppeteerPageHandlerBuilder
{
    private readonly IPuppeteerBrowserProvider _browserProvider;

    public LazyInitializationPageHandlerBuilder(IPuppeteerBrowserProvider browserProvider)
    {
        _browserProvider = browserProvider;
    }

    public PuppeteerBrowserSpecificaiton BrowserSpecification { get; set; } = new();

    public IPuppeteerPageHandler Build()
    {
        return new Handler(BrowserSpecification, _browserProvider);
    }

    sealed class Handler : IPuppeteerPageHandler, IAsyncDisposable
    {
        private readonly PuppeteerBrowserSpecificaiton _specificaiton;
        private readonly IPuppeteerBrowserProvider _browserProvider;
        private readonly SemaphoreSlim _createPageMutex = new(1, 1);
        private IPage? _page;

        public Handler(PuppeteerBrowserSpecificaiton specificaiton, IPuppeteerBrowserProvider browserProvider)
        {
            _specificaiton = specificaiton;
            _browserProvider = browserProvider;
        }

        public PuppeteerBrowserSpecificaiton BrowserSpecificaiton => _specificaiton;

        public ValueTask DisposeAsync()
        {
            var page = Interlocked.Exchange(ref _page, null);
            if (page is not null)
            {
                return page.DisposeAsync();
            }

            return default;
        }

        public ValueTask<IPage> GetPageAsync(CancellationToken cancellationToken = default)
        {
            var page = Volatile.Read(ref _page);
            if (page is not null)
            {
                return new(page);
            }

            return CreatePage(cancellationToken);
        }

        private async ValueTask<IPage> CreatePage(CancellationToken cancellationToken)
        {
            await _createPageMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _page) is { } page)
                {
                    return page;
                }
                var browser = await _browserProvider.GetBrowser(_specificaiton, cancellationToken).ConfigureAwait(false);
                page = await browser.NewPageAsync().ConfigureAwait(false);
                Interlocked.CompareExchange(ref _page, page, null);
                return page;
            }
            finally
            {
                _createPageMutex.Release();
            }
        }
    }

}

// Thread-safety: We treat this class as immutable except for the timer. Creating a new object
// for the 'expiry' pool simplifies the threading requirements significantly.
internal sealed class ActiveHandlerTrackingEntry
{
    private static readonly TimerCallback _timerCallback = (s) => ((ActiveHandlerTrackingEntry)s!).Timer_Tick();
    private readonly object _lock;
    private bool _timerInitialized;
    private System.Threading.Timer? _timer;
    private TimerCallback? _callback;

    public ActiveHandlerTrackingEntry(
        PuppeteerBrowserSpecificaiton browserSpecificaiton,
        LifetimeTrackingPageHandler handler,
        IServiceScope? scope,
        TimeSpan lifetime)
    {
        BrowserSpecificaiton = browserSpecificaiton;
        Handler = handler;
        Scope = scope;
        Lifetime = lifetime;

        _lock = new();
    }

    public LifetimeTrackingPageHandler Handler { get; private set; }

    public TimeSpan Lifetime { get; }

    public PuppeteerBrowserSpecificaiton BrowserSpecificaiton { get; }

    public IServiceScope? Scope { get; }

    public void StartExpiryTimer(TimerCallback callback)
    {
        if (Lifetime == Timeout.InfiniteTimeSpan)
        {
            return; // never expires.
        }

        if (Volatile.Read(ref _timerInitialized))
        {
            return;
        }

        StartExpiryTimerSlow(callback);
    }

    private void StartExpiryTimerSlow(TimerCallback callback)
    {
        Debug.Assert(Lifetime != Timeout.InfiniteTimeSpan);

        lock (_lock)
        {
            if (Volatile.Read(ref _timerInitialized))
            {
                return;
            }

            _callback = callback;
            _timer = NonCapturingTimer.Create(_timerCallback, this, Lifetime, Timeout.InfiniteTimeSpan);
            _timerInitialized = true;
        }
    }

    private void Timer_Tick()
    {
        Debug.Assert(_callback != null);
        Debug.Assert(_timer != null);

        lock (_lock)
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;

                _callback(this);
            }
        }
    }
}

// Thread-safety: This class is immutable
internal sealed class ExpiredHandlerTrackingEntry
{
    private readonly WeakReference _livenessTracker;

    // IMPORTANT: don't cache a reference to `other` or `other.Handler` here.
    // We need to allow it to be GC'ed.
    public ExpiredHandlerTrackingEntry(ActiveHandlerTrackingEntry other)
    {
        BrowserSpecificaiton = other.BrowserSpecificaiton;
        Scope = other.Scope;

        _livenessTracker = new WeakReference(other.Handler);
        InnerHandler = other.Handler.InnerHandler!;
    }

    public bool CanDispose => !_livenessTracker.IsAlive;

    public IPuppeteerPageHandler InnerHandler { get; }

    public PuppeteerBrowserSpecificaiton BrowserSpecificaiton { get; }

    public IServiceScope? Scope { get; }
}

public sealed class PuppeteerPageHandlerFactoryOptions : IOptions<PuppeteerPageHandlerFactoryOptions>
{
    public TimeSpan HandlerLifetime { get; set; } = TimeSpan.FromMinutes(2);
    public bool SuppressServiceScope { get; set; } = false;
    PuppeteerPageHandlerFactoryOptions IOptions<PuppeteerPageHandlerFactoryOptions>.Value => this;
}

internal class DefaultPuppeteerPageHandlerFactory : IPuppeteerPageHandlerFactory
{
    private static readonly TimerCallback _cleanupCallback = (s) => ((DefaultPuppeteerPageHandlerFactory)s!).CleanupTimer_Tick();
    private readonly IServiceProvider _services;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PuppeteerPageHandlerFactoryOptions> _options;
    private readonly Func<PuppeteerBrowserSpecificaiton, Lazy<ActiveHandlerTrackingEntry>> _entryFactory;
    private readonly Lazy<ILogger> _logger;

    // Default time of 10s for cleanup seems reasonable.
    // Quick math:
    // 10 distinct named clients * expiry time >= 1s = approximate cleanup queue of 100 items
    //
    // This seems frequent enough. We also rely on GC occurring to actually trigger disposal.
    private readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromSeconds(10);

    // We use a new timer for each regular cleanup cycle, protected with a lock. Note that this scheme
    // doesn't give us anything to dispose, as the timer is started/stopped as needed.
    //
    // There's no need for the factory itself to be disposable. If you stop using it, eventually everything will
    // get reclaimed.
    private System.Threading.Timer? _cleanupTimer;
    private readonly object _cleanupTimerLock = new();
    private readonly object _cleanupActiveLock = new();

    // Collection of 'active' handlers.
    //
    // Using lazy for synchronization to ensure that only one instance of HttpMessageHandler is created
    // for each name.
    //
    // internal for tests
    internal readonly ConcurrentDictionary<PuppeteerBrowserSpecificaiton, Lazy<ActiveHandlerTrackingEntry>> _activeHandlers = new();

    // Collection of 'expired' but not yet disposed handlers.
    //
    // Used when we're rotating handlers so that we can dispose HttpMessageHandler instances once they
    // are eligible for garbage collection.
    //
    // internal for tests
    internal readonly ConcurrentQueue<ExpiredHandlerTrackingEntry> _expiredHandlers = new();
    private readonly TimerCallback _expiryCallback;

    public DefaultPuppeteerPageHandlerFactory(
        IServiceProvider services,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PuppeteerPageHandlerFactoryOptions> options)
    {
        _services = services;
        _scopeFactory = scopeFactory;
        _options = options;

        _entryFactory = (browserSpecification) =>
        {
            return new Lazy<ActiveHandlerTrackingEntry>(() =>
            {
                return CreateHandlerEntry(browserSpecification);
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        };

        _expiryCallback = ExpiryTimer_Tick;

        // We want to prevent a circular depencency between ILoggerFactory and IHttpClientFactory, in case
        // any of ILoggerProvider instances use IHttpClientFactory to send logs to an external server.
        // Logger will be created during the first ExpiryTimer_Tick execution. Lazy guarantees thread safety
        // to prevent creation of unnecessary ILogger objects in case several handlers expired at the same time.
        _logger = new Lazy<ILogger>(
            () => _services.GetRequiredService<ILoggerFactory>().CreateLogger<DefaultPuppeteerPageHandlerFactory>(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IPuppeteerPageHandler CreateHandler(PuppeteerBrowserSpecificaiton browserSpecification)
    {
        ActiveHandlerTrackingEntry entry = _activeHandlers.GetOrAdd(browserSpecification, _entryFactory).Value;

        StartHandlerEntryTimer(entry);

        return entry.Handler;
    }

    // Internal for tests
    internal ActiveHandlerTrackingEntry CreateHandlerEntry(PuppeteerBrowserSpecificaiton browserSpecification)
    {
        var options = _options.CurrentValue;

        IServiceProvider services = _services;
        var scope = (IServiceScope?)null;

        if (!options.SuppressServiceScope)
        {
            scope = _scopeFactory.CreateScope();
            services = scope.ServiceProvider;
        }

        try
        {
            var builder = services.GetRequiredService<IPuppeteerPageHandlerBuilder>();
            // Wrap the handler so we can ensure the inner handler outlives the outer handler.
            LifetimeTrackingPageHandler handler = new(builder.Build());

            // Note that we can't start the timer here. That would introduce a very very subtle race condition
            // with very short expiry times. We need to wait until we've actually handed out the handler once
            // to start the timer.
            //
            // Otherwise it would be possible that we start the timer here, immediately expire it (very short
            // timer) and then dispose it without ever creating a client. That would be bad. It's unlikely
            // this would happen, but we want to be sure.
            return new ActiveHandlerTrackingEntry(handler.BrowserSpecificaiton, handler, scope, options.HandlerLifetime);
        }
        catch
        {
            // If something fails while creating the handler, dispose the services.
            scope?.Dispose();
            throw;
        }
    }

    // Internal for tests
    internal void ExpiryTimer_Tick(object? state)
    {
        var active = (ActiveHandlerTrackingEntry)state!;

        // The timer callback should be the only one removing from the active collection. If we can't find
        // our entry in the collection, then this is a bug.
        bool removed = _activeHandlers.TryRemove(active.BrowserSpecificaiton, out Lazy<ActiveHandlerTrackingEntry>? found);
        Debug.Assert(removed, "Entry not found. We should always be able to remove the entry");
        Debug.Assert(object.ReferenceEquals(active, found!.Value), "Different entry found. The entry should not have been replaced");

        // At this point the handler is no longer 'active' and will not be handed out to any new clients.
        // However we haven't dropped our strong reference to the handler, so we can't yet determine if
        // there are still any other outstanding references (we know there is at least one).
        //
        // We use a different state object to track expired handlers. This allows any other thread that acquired
        // the 'active' entry to use it without safety problems.
        var expired = new ExpiredHandlerTrackingEntry(active);
        _expiredHandlers.Enqueue(expired);

        Log.HandlerExpired(_logger, active.BrowserSpecificaiton, active.Lifetime);

        StartCleanupTimer();
    }

    // Internal so it can be overridden in tests
    internal virtual void StartHandlerEntryTimer(ActiveHandlerTrackingEntry entry)
    {
        entry.StartExpiryTimer(_expiryCallback);
    }

    // Internal so it can be overridden in tests
    internal virtual void StartCleanupTimer()
    {
        lock (_cleanupTimerLock)
        {
            _cleanupTimer ??= NonCapturingTimer.Create(_cleanupCallback, this, DefaultCleanupInterval, Timeout.InfiniteTimeSpan);
        }
    }

    // Internal so it can be overridden in tests
    internal virtual void StopCleanupTimer()
    {
        lock (_cleanupTimerLock)
        {
            _cleanupTimer!.Dispose();
            _cleanupTimer = null;
        }
    }

    // Internal for tests
    internal void CleanupTimer_Tick()
    {
        // Stop any pending timers, we'll restart the timer if there's anything left to process after cleanup.
        //
        // With the scheme we're using it's possible we could end up with some redundant cleanup operations.
        // This is expected and fine.
        //
        // An alternative would be to take a lock during the whole cleanup process. This isn't ideal because it
        // would result in threads executing ExpiryTimer_Tick as they would need to block on cleanup to figure out
        // whether we need to start the timer.
        StopCleanupTimer();

        if (!Monitor.TryEnter(_cleanupActiveLock))
        {
            // We don't want to run a concurrent cleanup cycle. This can happen if the cleanup cycle takes
            // a long time for some reason. Since we're running user code inside Dispose, it's definitely
            // possible.
            //
            // If we end up in that position, just make sure the timer gets started again. It should be cheap
            // to run a 'no-op' cleanup.
            StartCleanupTimer();
            return;
        }

        try
        {
            int initialCount = _expiredHandlers.Count;
            Log.CleanupCycleStart(_logger, initialCount);

            var stopwatch = ValueStopwatch.StartNew();

            int disposedCount = 0;
            for (int i = 0; i < initialCount; i++)
            {
                // Since we're the only one removing from _expired, TryDequeue must always succeed.
                _expiredHandlers.TryDequeue(out ExpiredHandlerTrackingEntry? entry);
                Debug.Assert(entry != null, "Entry was null, we should always get an entry back from TryDequeue");

                if (entry.CanDispose)
                {
                    try
                    {
                        if (entry.InnerHandler is IAsyncDisposable asyncDisposable)
                        {
                            asyncDisposable.DisposeAsync();
                        }
                        entry.Scope?.Dispose();
                        disposedCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.CleanupItemFailed(_logger, entry.BrowserSpecificaiton, ex);
                    }
                }
                else
                {
                    // If the entry is still live, put it back in the queue so we can process it
                    // during the next cleanup cycle.
                    _expiredHandlers.Enqueue(entry);
                }
            }

            Log.CleanupCycleEnd(_logger, stopwatch.GetElapsedTime(), disposedCount, _expiredHandlers.Count);
        }
        finally
        {
            Monitor.Exit(_cleanupActiveLock);
        }

        // We didn't totally empty the cleanup queue, try again later.
        if (!_expiredHandlers.IsEmpty)
        {
            StartCleanupTimer();
        }
    }

    private static class Log
    {
        public static class EventIds
        {
            public static readonly EventId CleanupCycleStart = new EventId(100, "CleanupCycleStart");
            public static readonly EventId CleanupCycleEnd = new EventId(101, "CleanupCycleEnd");
            public static readonly EventId CleanupItemFailed = new EventId(102, "CleanupItemFailed");
            public static readonly EventId HandlerExpired = new EventId(103, "HandlerExpired");
        }

        private static readonly Action<ILogger, int, Exception?> _cleanupCycleStart = LoggerMessage.Define<int>(
            LogLevel.Debug,
            EventIds.CleanupCycleStart,
            "Starting HttpMessageHandler cleanup cycle with {InitialCount} items");

        private static readonly Action<ILogger, double, int, int, Exception?> _cleanupCycleEnd = LoggerMessage.Define<double, int, int>(
            LogLevel.Debug,
            EventIds.CleanupCycleEnd,
            "Ending HttpMessageHandler cleanup cycle after {ElapsedMilliseconds}ms - processed: {DisposedCount} items - remaining: {RemainingItems} items");

        private static readonly Action<ILogger, PuppeteerBrowserSpecificaiton, Exception> _cleanupItemFailed = LoggerMessage.Define<PuppeteerBrowserSpecificaiton>(
            LogLevel.Error,
            EventIds.CleanupItemFailed,
            "HttpMessageHandler.Dispose() threw an unhandled exception for client: '{ClientName}'");

        private static readonly Action<ILogger, double, PuppeteerBrowserSpecificaiton, Exception?> _handlerExpired = LoggerMessage.Define<double, PuppeteerBrowserSpecificaiton>(
            LogLevel.Debug,
            EventIds.HandlerExpired,
            "HttpMessageHandler expired after {HandlerLifetime}ms for client '{ClientName}'");


        public static void CleanupCycleStart(Lazy<ILogger> loggerLazy, int initialCount)
        {
            if (TryGetLogger(loggerLazy, out ILogger? logger))
            {
                _cleanupCycleStart(logger, initialCount, null);
            }
        }

        public static void CleanupCycleEnd(Lazy<ILogger> loggerLazy, TimeSpan duration, int disposedCount, int finalCount)
        {
            if (TryGetLogger(loggerLazy, out ILogger? logger))
            {
                _cleanupCycleEnd(logger, duration.TotalMilliseconds, disposedCount, finalCount, null);
            }
        }

        public static void CleanupItemFailed(Lazy<ILogger> loggerLazy, PuppeteerBrowserSpecificaiton browserSpecification, Exception exception)
        {
            if (TryGetLogger(loggerLazy, out ILogger? logger))
            {
                _cleanupItemFailed(logger, browserSpecification, exception);
            }
        }

        public static void HandlerExpired(Lazy<ILogger> loggerLazy, PuppeteerBrowserSpecificaiton browserSpecification, TimeSpan lifetime)
        {
            if (TryGetLogger(loggerLazy, out ILogger? logger))
            {
                _handlerExpired(logger, lifetime.TotalMilliseconds, browserSpecification, null);
            }
        }

        private static bool TryGetLogger(Lazy<ILogger> loggerLazy, [NotNullWhen(true)] out ILogger? logger)
        {
            logger = null;
            try
            {
                logger = loggerLazy.Value;
            }
            catch { } // not throwing in logs

            return logger is not null;
        }
    }
}

internal sealed class RawPuppeteerBrowserPageLoader : IRawBrowserPageLoader
{
    private readonly IPuppeteerPageHandler _handler;
    private readonly ICookiesStorage _cookiesStorage;

    public RawPuppeteerBrowserPageLoader(IPuppeteerPageHandler handler, ICookiesStorage cookiesStorage)
    {
        _handler = handler;
        _cookiesStorage = cookiesStorage;
    }

    public async Task<HttpContent> LoadAsync(BrowserPageLoadParameter pageParameter, CancellationToken cancellationToken = default)
    {
        var page = await _handler.GetPageAsync(cancellationToken).ConfigureAwait(false);
        await ApplyCookiesStorage().ConfigureAwait(false);
        await ApplyNavigation().ConfigureAwait(false);
        await ApplyPageActionsAsync().ConfigureAwait(false);
        return await GetContentAsync().ConfigureAwait(false);

        async Task ApplyPageActionsAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var action in pageParameter.PageActions)
            {
                await action.ApplyAsync(page, cancellationToken).ConfigureAwait(false);
            }
        }
        async Task ApplyNavigation()
        {
            cancellationToken.ThrowIfCancellationRequested();
            await page.GoToAsync(pageParameter.Url.ToString(), new NavigationOptions() { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });
        }
        async Task ApplyCookiesStorage()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cookies = await _cookiesStorage.GetAsync(cancellationToken).ConfigureAwait(false);
            await page.SetCookieAsync(cookies
                .GetAllCookies()
                .Select(cookie => cookie.ToPuppeteerCookie())
                .ToArray()).ConfigureAwait(false);

        }
        async Task<HttpContent> GetContentAsync()
        {
            var content = await page.GetContentAsync().ConfigureAwait(false);
            return new StringContent(content);
        }
    }
}

internal sealed class PollyPuppeteerBrowserPageLoader : IBrowserPageLoader
{
    private readonly IRawBrowserPageLoader _rawBrowserPageLoader;
    private readonly ILogger _logger;
    private readonly PageLoaderOptions _options;

    public PollyPuppeteerBrowserPageLoader(IRawBrowserPageLoader rawBrowserPageLoader, ILogger<PollyPuppeteerBrowserPageLoader> logger, IOptions<PageLoaderOptions> options)
    {
        _rawBrowserPageLoader = rawBrowserPageLoader;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<HttpContent> LoadAsync(BrowserPageLoadParameter parameter, CancellationToken cancellationToken = default)
    {
        using var _ = _logger.LogMethodDuration();
        var policy = _options.RequestPolicy ?? Policy.NoOpAsync();
        var content = await policy.ExecuteAsync(async cancellationToken => await _rawBrowserPageLoader.LoadAsync(parameter, cancellationToken).ConfigureAwait(false), cancellationToken, continueOnCapturedContext: false).ConfigureAwait(false);
        return content;
    }
}
