using System.Net.Mime;
using System.Reflection.Metadata;

namespace RedditDotnetScraper;

public static class EnumerableExtensions
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class
    {
        foreach (var item in source)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : struct
    {
        foreach (var item in source)
        {
            if (item is not null)
            {
                yield return item.Value;
            }
        }
    }

    public static IEnumerable<U> SelectNotNull<T, U>(this IEnumerable<T> source, Func<T, U?> selector) where U : class
    {
        foreach (var item in source)
        {
            var result = selector(item);
            if (result is not null)
            {
                yield return result;
            }
        }
    }

    public static IEnumerable<U> SelectNotNull<T, U>(this IEnumerable<T> source, Func<T, U?> selector) where U : struct
    {
        foreach (var item in source)
        {
            var result = selector(item);
            if (result is not null)
            {
                yield return result.Value;
            }
        }
    }

    public static IEnumerable<U> Select<T, U>(this IEnumerable<T> source, Func<T, U> selector, IExceptionHandler exceptionHandler)
    {
        foreach (var item in source)
        {
            var hasResult = false;
            U result = default!;
            try
            {
                result = selector(item);
                hasResult = true;
            }
            catch (Exception exception) when (exceptionHandler.TryHandle(exception))
            {
            }
            if (hasResult)
            {
                yield return result;
            }
        }
    }
}

public interface IExceptionHandler
{
    bool TryHandle(Exception exception);

    static IExceptionHandler Handle<TException>(Action<TException> handler) where TException : Exception
    {
        return new ExceptionHandler<TException>(handler);
    }

    static IExceptionHandler Handle(Action<Exception> handle) => Handle<Exception>(handle);
}

internal sealed class ExceptionHandler<TException> : IExceptionHandler where TException : Exception
{
    private readonly Action<TException> _handler;

    public ExceptionHandler(Action<TException> handler)
    {
        _handler = handler;
    }

    public bool TryHandle(Exception exception)
    {
        if (exception is TException tException)
        {
            _handler(tException);
            return true;
        }

        return false;
    }
}