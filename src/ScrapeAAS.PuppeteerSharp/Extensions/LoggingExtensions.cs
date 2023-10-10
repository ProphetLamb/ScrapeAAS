using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ScrapeAAS.Utility;

namespace ScrapeAAS.Extensions;

internal static class LoggerExtensions
{
    public static IDisposable LogMethodDuration(
        this ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        return new Timer(logger, callerName);
    }

    public static void LogInvocationCount(
        this ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        _ = logger;
        _ = callerName;
        // TODO: Implement
    }
}

internal class Timer(
    ILogger logger,
    [CallerMemberName] string callerName = "") : IDisposable
{
    private readonly ILogger _logger = logger;
    private readonly string _callerName = callerName;
    private readonly ValueStopwatch _stopwatch = ValueStopwatch.StartNew();

    public void Dispose()
    {
        var elapsed = _stopwatch.GetElapsedTime();
        _logger.LogInformation("{Method} finished in {Elapsed}", _callerName, elapsed);
    }
}