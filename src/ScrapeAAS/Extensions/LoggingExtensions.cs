using System;
using System.Diagnostics;
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
        // TODO: Implement
    }
}

internal class Timer : IDisposable
{
    private readonly ILogger _logger;
    private readonly string callerName = "";
    private readonly ValueStopwatch _stopwatch;

    public Timer(
        ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        _logger = logger;

        _stopwatch = ValueStopwatch.StartNew();

        this.callerName = callerName;
    }

    public void Dispose()
    {
        TimeSpan elapsed = _stopwatch.GetElapsedTime();
        _logger.LogInformation("{Method} finished in {Elapsed}", callerName, elapsed);
    }
}