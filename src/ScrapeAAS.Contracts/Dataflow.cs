using GlobExpressions;

namespace ScrapeAAS.Contracts;

/// <summary>
/// Inflates the dataflow for the type <typeparamref name="T"/> with data.
/// </summary>
/// <typeparam name="T">The type of the dataflow.</typeparam>
public interface IDataSourceInflator<T>
{
    /// <summary>
    /// Collects a single element from the data source
    /// </summary>
    /// <param name="message">The element to collect</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task that completes when the element has been collected</returns>
    ValueTask AddAsync(T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes all datasources for the dataflow for the type <typeparamref name="T"/>
    /// </summary>
    /// <param name="error">The error that caused the completion</param>
    void Complete(Exception? error = null);
}

/// <summary>
/// Inflates the dataflow for the type <typeparamref name="T"/> with data, by calling <see cref="IDataSourceInflator{T}.AddToFirstAsync(T, CancellationToken)"/>.
/// </summary>
/// <typeparam name="T">The type of the dataflow.</typeparam>
public interface IDataflowProvider<T>
{
    /// <summary>
    /// Executes the data provider
    /// </summary>
    /// <param name="inflator">The inflator to inflate the dataflow with</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task that completes when the data provider has completed</returns>
    Task ExecuteAsync(IDataSourceInflator<T> inflator, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consumes data from the dataflow for the type <typeparamref name="T"/>
/// </summary>
public interface IDataflowConsumer<T>
{
    /// <summary>
    /// Consumes a single element from the dataflow
    /// </summary>
    /// <param name="element">The element to consume</param>
    /// <param name="cancellationToken">The cancellation token</param>
    Task ConsumeAsync(T element, CancellationToken cancellationToken = default);
}

/// <summary>
/// Part of the dataflow for the type <typeparamref name="T"/>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DataflowAttribute : Attribute
{
    /// <summary>
    /// The concrete key of the dataflow, or null if the dataflow is not keyed
    /// </summary>
    /// <seealso cref="DataflowKey"/>
    public string? Key { get; set; }
}