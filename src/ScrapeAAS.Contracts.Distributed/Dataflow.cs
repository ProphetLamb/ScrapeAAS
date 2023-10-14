using System;

namespace ScrapeAAS;

/// <summary>
/// Inflates the distributed dataflow for the type <typeparamref name="T"/> with data.
/// </summary>
/// <remarks>
/// All data published using a <see cref="IDistributedDataflowPublisher{T}"/> must be conumed by a <see cref="IDistributedDataflowHandler{T}"/>.
/// </remarks>
/// <typeparam name="T">The type of the dataflow.</typeparam>
public interface IDistributedDataflowPublisher<T> : IDataflowPublisher<T>
{

}

/// <summary>
/// Defines a handler for processing data elements from a data source.
/// </summary>
/// <remarks>
/// All data published using a <see cref="IDistributedDataflowPublisher{T}"/> must be conumed by a <see cref="IDistributedDataflowHandler{T}"/>.
/// </remarks>
/// <typeparam name="T">The type of data element to handle.</typeparam>
public interface IDistributedDataflowHandler<T> : IDataflowHandler<T>
{

}