namespace RedditDotnetScraper;

public static class EnumerableExtensions
{
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
            catch (Exception exception) when (exceptionHandler.TryHandle(exception, item))
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
    bool TryHandle(Exception exception, object? item);

    /// <summary>
    /// Creates an instance of <see cref="IExceptionHandler"/> that handles the specified exception type with the given handler.
    /// </summary>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <param name="handler">The handler to execute when the specified exception type is caught.</param>
    /// <returns>An instance of <see cref="IExceptionHandler"/> that handles the specified exception type with the given handler.</returns>
    static IExceptionHandler Handle<TException>(Action<TException> handler) where TException : Exception
    {
        return new ExceptionHandler<TException>(handler);
    }

    /// <summary>
    /// Creates an exception handler for a specific exception type.
    /// </summary>
    /// <typeparam name="TException">The type of exception to handle.</typeparam>
    /// <param name="handler">The action to perform on the exception and the failed item when the exception is handled.</param>
    /// <returns>An <see cref="IExceptionHandler"/> instance that can handle the specified exception type.</returns>
    static IExceptionHandler Handle<TException>(Action<TException, object?> handler) where TException : Exception
    {
        return new ExceptionHandler<TException>(handler);
    }

    /// <summary>
    /// Returns an blanket <see cref="IExceptionHandler"/> instance that handles all exceptions.
    /// </summary>
    /// <param name="handle">The action to perform when any exception is thrown.</param>
    /// <returns>An <see cref="IExceptionHandler"/> instance that handles all exceptions.</returns>
    static IExceptionHandler Handle(Action<Exception> handle) => Handle<Exception>(handle);

    /// <summary>
    /// Returns an blanket <see cref="IExceptionHandler"/> instance that handles all exceptions.
    /// </summary>
    /// <param name="handle">The action to perform on the action and the failed item when any exception is thrown.</param>
    /// <returns>An <see cref="IExceptionHandler"/> instance that handles all exceptions.</returns>
    static IExceptionHandler Handle(Action<Exception, object?> handle) => Handle<Exception>(handle);
}

internal sealed class ExceptionHandler<TException> : IExceptionHandler where TException : Exception
{
    private readonly Action<TException>? _handler;
    private readonly Action<TException, object?>? _handlerWithItem;

    public ExceptionHandler(Action<TException> handler)
    {
        _handler = handler;
        _handlerWithItem = null;
    }

    public ExceptionHandler(Action<TException, object?> handler)
    {
        _handler = null;
        _handlerWithItem = handler;
    }

    public bool TryHandle(Exception exception, object? item)
    {
        if (exception is TException strongException)
        {
            if (_handlerWithItem is not null)
            {
                _handlerWithItem(strongException, item);
                return true;
            }
            else if (_handler is not null)
            {
                _handler(strongException);
                return true;
            }
        }

        return false;
    }
}