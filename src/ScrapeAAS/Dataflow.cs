using System.Threading.Channels;
using ScrapeAAS.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace ScrapeAAS;

public sealed class DataflowMultiplexerChannelOptions
{
    private readonly UnboundedChannelOptions? _unboundedChannelOptions;
    private readonly BoundedChannelOptions? _boundedChannelOptions;

    public DataflowMultiplexerChannelOptions(UnboundedChannelOptions unboundedChannelOptions)
    {
        _unboundedChannelOptions = unboundedChannelOptions;
    }

    public DataflowMultiplexerChannelOptions(BoundedChannelOptions boundedChannelOptions)
    {
        _boundedChannelOptions = boundedChannelOptions;
    }

    public static DataflowMultiplexerChannelOptions Default => new(new BoundedChannelOptions(512)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    public Channel<T> CreateChannel<T>()
    {
        if (_unboundedChannelOptions != null)
        {
            return Channel.CreateUnbounded<T>(_unboundedChannelOptions);
        }

        if (_boundedChannelOptions != null)
        {
            return Channel.CreateBounded<T>(_boundedChannelOptions);
        }

        throw new InvalidOperationException("ChannelConfiguration is not configured");
    }
}

public sealed class DataflowMultiplexerDeflatorOptions
{
    public AsyncPolicy? DeflatorFailurePolicy { get; set; }

    public static DataflowMultiplexerDeflatorOptions Default => new()
    {
        DeflatorFailurePolicy = Policy.NoOpAsync()
    };
}

public sealed class DataflowMultiplexerOptions : IOptions<DataflowMultiplexerOptions>
{
    public DataflowMultiplexerChannelOptions? ChannelConfiguration { get; set; }
    public DataflowMultiplexerDeflatorOptions? DeflatorOptions { get; set; }
    DataflowMultiplexerOptions IOptions<DataflowMultiplexerOptions>.Value => this;
}

public sealed class DataflowMultiplexer<T> : BackgroundService
{
    private readonly IEnumerable<IDataflowProvider<T>> _dataProviders;
    private readonly IEnumerable<IDataflowConsumer<T>> _dataConsumers;
    private readonly DataflowMultiplexerOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private WeakReference<Channel<T>>? _weakChannel;

    public DataflowMultiplexer(IEnumerable<IDataflowProvider<T>> dataProviders, IEnumerable<IDataflowConsumer<T>> dataConsumers, IOptions<DataflowMultiplexerOptions> options, ILoggerFactory loggerFactory)
    {
        _dataProviders = dataProviders;
        _dataConsumers = dataConsumers;
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DataflowMultiplexer<T>>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting dataflow multiplexer for type {Type}", typeof(T).FullName);

        var deflatorOptions = _options.DeflatorOptions ?? DataflowMultiplexerDeflatorOptions.Default;
        var channel = (_options.ChannelConfiguration ?? DataflowMultiplexerChannelOptions.Default).CreateChannel<T>();
        _weakChannel = new(channel);
        var dataProviderExecutors = _dataProviders.Select(x => new DataflowChannelProviderExecutor<T>(x, channel.Writer, _loggerFactory.CreateLogger<DataflowChannelProviderExecutor<T>>()));
        var dataConsumerExecutors = _dataConsumers.Select(x => new DataflowChannelDeflatorExecutor<T>(x, channel.Reader, _loggerFactory.CreateLogger<DataflowChannelDeflatorExecutor<T>>(), deflatorOptions));
        await Task.WhenAll(dataProviderExecutors.Select(x => x.ExecuteAsync(stoppingToken)).Concat(dataConsumerExecutors.Select(x => x.ExecuteAsync(stoppingToken)))).ConfigureAwait(false);
        channel.Writer.TryComplete();

        _logger.LogInformation("Completed dataflow multiplexer for type {Type}", typeof(T).FullName);
    }

    internal Channel<T>? GetChannelOrDefault()
    {
        if (_weakChannel is null || !_weakChannel.TryGetTarget(out var channel))
        {
            return null;
        }
        return channel;
    }
}

internal sealed class DataflowChannelProviderExecutor<T>
{
    private readonly IDataflowProvider<T> _dataProvider;
    private readonly ChannelWriter<T> _channelWriter;
    private readonly ILogger _logger;

    public DataflowChannelProviderExecutor(IDataflowProvider<T> dataProvider, ChannelWriter<T> channelWriter, ILogger<DataflowChannelProviderExecutor<T>> logger)
    {
        _dataProvider = dataProvider;
        _channelWriter = channelWriter;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing data provider {DataProvider}", _dataProvider.GetType().FullName);
        DataflowChannelInflator<T> inflator = new(_channelWriter);
        try
        {
            await _dataProvider.ExecuteAsync(inflator, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning("{DataProvider} Attempted to write to a closed channel", _dataProvider.GetType().FullName);
        }
        catch (OperationCanceledException)
        {
            inflator.Complete();
        }
        catch (Exception ex)
        {
            inflator.Complete(ex);
            _logger.LogError(ex, "Error executing data provider {DataProvider}", _dataProvider.GetType().FullName);
        }
        _logger.LogInformation("Completed data provider {DataProvider}", _dataProvider.GetType().FullName);
    }
}

internal sealed class DataflowChannelInflator<T> : IDataSourceInflator<T>
{
    private readonly ChannelWriter<T> _channelWriter;

    public DataflowChannelInflator(ChannelWriter<T> channelWriter)
    {
        _channelWriter = channelWriter;
    }

    public ValueTask AddAsync(T element, CancellationToken cancellationToken = default)
    {
        return _channelWriter.WriteAsync(element, cancellationToken);
    }

    public void Complete(Exception? error = null)
    {
        _channelWriter.Complete(error);
    }
}

internal sealed class DataflowChannelDeflatorExecutor<T>
{
    private readonly IDataflowConsumer<T> _dataConsumer;
    private readonly ChannelReader<T> _channelReader;
    private readonly ILogger _logger;
    private readonly AsyncPolicy _deflatorFailurePolicy;

    public DataflowChannelDeflatorExecutor(IDataflowConsumer<T> dataConsumer, ChannelReader<T> channelReader, ILogger<DataflowChannelDeflatorExecutor<T>> logger, DataflowMultiplexerDeflatorOptions options)
    {
        _dataConsumer = dataConsumer;
        _channelReader = channelReader;
        _logger = logger;
        _deflatorFailurePolicy = options.DeflatorFailurePolicy ?? Policy.NoOpAsync();
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing data consumer {DataConsumer}", _dataConsumer.GetType().FullName);
        while (!cancellationToken.IsCancellationRequested)
        {
            var (readSuccess, element) = await TryReadAsync(cancellationToken).ConfigureAwait(false);
            if (!readSuccess)
            {
                break;
            }
            var consumeResult = await _deflatorFailurePolicy.ExecuteAndCaptureAsync(
                stoppingToken => TryConsumeAsync(element, stoppingToken),
                cancellationToken,
                continueOnCapturedContext: false
            ).ConfigureAwait(false);
            if (consumeResult.Outcome == OutcomeType.Failure)
            {
                _logger.LogError(consumeResult.FinalException, "Error consuming element {DataConsumer}", _dataConsumer.GetType().FullName);
                break;
            }
            if (!consumeResult.Result)
            {
                break;
            }
        }
        _logger.LogInformation("Completed data consumer {DataConsumer}", _dataConsumer.GetType().FullName);
    }

    private async Task<bool> TryConsumeAsync(T element, CancellationToken stoppingToken)
    {
        try
        {
            await _dataConsumer.ConsumeAsync(element, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming element {DataConsumer}", _dataConsumer.GetType().FullName);
            throw;
        }
        return true;
    }

    private async Task<(bool Success, T Element)> TryReadAsync(CancellationToken stoppingToken)
    {
        T element;
        try
        {
            element = await _channelReader.ReadAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return default;
        }
        catch (OperationCanceledException)
        {
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from channel");
            return default;
        }

        return (true, element);
    }
}


public interface IDataflowReader<T>
{
    /// <summary>
    /// Asynchronously reads a value of type <typeparamref name="T"/> from the dataflow.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to use.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> representing the asynchronous operation. The result of the task contains the value read from the dataflow.</returns>
    /// <exception cref="ChannelClosedException">The dataflow is not running.</exception>
    ValueTask<T> ReadAsync(CancellationToken cancellationToken = default);
}

internal sealed class DataflowReader<T> : IDataflowReader<T>
{
    private readonly DataflowMultiplexer<T> _multiplexer;

    public DataflowReader(DataflowMultiplexer<T> multiplexer)
    {
        _multiplexer = multiplexer;
    }

    public ValueTask<T> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_multiplexer.GetChannelOrDefault() is not { } channel)
        {
            throw new ChannelClosedException("DataflowMultiplexer is not running");
        }
        return channel.Reader.ReadAsync(cancellationToken);
    }
}

public static class DataflowExtensions
{
    public static IServiceCollection AddDataFlow(this IServiceCollection services)
    {
        return services.AddDataFlow(Assembly.GetExecutingAssembly());
    }

    private static IServiceCollection AddDataFlow(this IServiceCollection services, Assembly assemblyToScan)
    {
        var dataFlowTypes = assemblyToScan.GetExportedTypes()
            .Where(x => x.IsClass && !x.IsAbstract && x.CustomAttributes.Any(y => y.AttributeType == typeof(DataflowAttribute)))
            .ToImmutableArray();
        var dataProviders = RegisterTransientByGenericInterfaceOneParameter(services, dataFlowTypes, typeof(IDataflowProvider<>));
        var dataConsumers = RegisterTransientByGenericInterfaceOneParameter(services, dataFlowTypes, typeof(IDataflowConsumer<>));
        // union of providers and consumers
        foreach (var dataFlowType in dataProviders.Intersect(dataConsumers))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(DataflowMultiplexer<>).MakeGenericType(dataFlowType), typeof(DataflowMultiplexer<>).MakeGenericType(dataFlowType)));
            services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IDataflowReader<>).MakeGenericType(dataFlowType), typeof(DataflowReader<>).MakeGenericType(dataFlowType)));
        }
        return services;

        static IEnumerable<Type> RegisterTransientByGenericInterfaceOneParameter(IServiceCollection services, ImmutableArray<Type> implementationTypes, Type genericInterfaceType)
        {
            foreach (var (implementationType, interfaceTypes) in implementationTypes.Select(x => (x, InterfacesOfGenericTypeDefinition(x, genericInterfaceType))))
            {
                foreach (var interfaceType in interfaceTypes)
                {
                    services.TryAddEnumerable(ServiceDescriptor.Transient(interfaceType, implementationType));
                    yield return interfaceType.GetGenericArguments()[0];
                }
            }
        }

        static Type[] InterfacesOfGenericTypeDefinition(Type type, Type genericInterfaceTypeDefinition)
        {
            return type.FindInterfaces((t, genericInterfaceTypeDefinition) => t.IsGenericType && t.GetGenericTypeDefinition() == (Type)genericInterfaceTypeDefinition!, genericInterfaceTypeDefinition);
        }
    }
}