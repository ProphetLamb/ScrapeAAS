namespace ScrapeAAS.Contracts;

/// <summary>
/// Inflates the dataflow for the type <typeparamref name="T"/> with data.
/// </summary>
/// <typeparam name="T">The type of the dataflow.</typeparam>
public interface IDataflowPublisher<T>
{
    /// <summary>
    /// Collects a single element from the data source
    /// </summary>
    /// <param name="message">The element to collect</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task that completes when the element has been collected</returns>
    ValueTask PublishAsync(T message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a handler for processing data elements from a data source.
/// </summary>
/// <typeparam name="T">The type of data element to handle.</typeparam>
public interface IDataflowHandler<T>
{
    /// <summary>
    /// Handles a single element from the data source.
    /// </summary>
    /// <param name="message">The element to handle.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the element has been handled.</returns>
    ValueTask HandleAsync(T message, CancellationToken cancellationToken = default);
}
