using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

public interface IScrapeAASConfiguration : IEnumerable<ScrapeAASRole>
{
    ServiceLifetime LongLivingServiceLifetime { get; }
    IScrapeAASConfiguration WithLongLivingServiceLifetime(ServiceLifetime lifetime);

    void Use(ScrapeAASUsecase usecase);

    void Use(ScrapeAASRole role, Action<IScrapeAASConfiguration, IServiceCollection> configureServices)
    {
        Use(new(role, configureServices));
    }

    void Use(ScrapeAASRole role, Action<IServiceCollection> configureServices)
    {
        Use(new(role, (config, services) => configureServices(services)));
    }

    void Add(Action<IScrapeAASConfiguration, IServiceCollection> configureServices)
    {
        Use(new(default, configureServices));
    }

    Action<IScrapeAASConfiguration, IServiceCollection>? GetConfigurationOrDefault(ScrapeAASRole role);

    Action<IScrapeAASConfiguration, IServiceCollection> GetConfiguration(ScrapeAASRole role)
    {
        return GetConfigurationOrDefault(role) ?? throw new KeyNotFoundException($"The role {role} does not exist in the configuration");
    }

    void Build(IServiceCollection services);
}

public readonly record struct ScrapeAASRole(string Value)
{
    public static implicit operator ScrapeAASRole(string value) => new(value);

    public static ScrapeAASRole CookieStorage => "cookiestorage";
    public static ScrapeAASRole Dataflow => "dataflow";
    public static ScrapeAASRole StaticPageLoader => "pageloader-static";
    public static ScrapeAASRole BrowserPageLoader => "pageloader-browser";
    public static ScrapeAASRole ProxyProvider => "proxyprovider";

    public bool IsDefault => string.IsNullOrEmpty(Value);

    public static readonly ImmutableArray<ScrapeAASRole> RequiredRoles = ImmutableArray.Create([
        CookieStorage,
        Dataflow,
        StaticPageLoader,
        BrowserPageLoader,
        ProxyProvider,
    ]);
}

public readonly record struct ScrapeAASUsecase(ScrapeAASRole Role, Action<IScrapeAASConfiguration, IServiceCollection> ConfigureServices);

internal sealed class DefaultScrapeAASConfiguration : IScrapeAASConfiguration
{
    private readonly List<ScrapeAASUsecase> _usecases = [];
    private bool _readonly = false;

    public ServiceLifetime LongLivingServiceLifetime { get; set; } = ServiceLifetime.Scoped;

    public IScrapeAASConfiguration WithLongLivingServiceLifetime(ServiceLifetime lifetime)
    {
        ThrowIfReadonly();
        if (lifetime is not ServiceLifetime.Singleton or ServiceLifetime.Scoped)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), $"The long living service lifetime can only be {nameof(ServiceLifetime.Scoped)} or {nameof(ServiceLifetime.Singleton)}.");
        }
        return this;
    }

    public void Use(ScrapeAASUsecase usecase)
    {
        ThrowIfReadonly();
        if (usecase.Role.IsDefault)
        {
            _usecases.Add(usecase);
            return;
        }
        var existing = _usecases.FindIndex(u => u.Role == usecase.Role);
        if (existing >= 0)
        {
            _usecases[existing] = usecase;
            return;
        }

        _usecases.Add(usecase);
    }

    public void Build(IServiceCollection services)
    {
        EnsureRolesValid();
        LockReadonly();
        foreach (var usercase in _usecases)
        {
            usercase.ConfigureServices(this, services);
        }
    }

    private void LockReadonly()
    {
        _readonly = true;
    }

    private void ThrowIfReadonly()
    {
        if (_readonly)
        {
            throw new InvalidOperationException("The configuration is readonly.");
        }
    }

    private void EnsureRolesValid()
    {
        var roles = _usecases.Select(usercase => usercase.Role).ToHashSet();
        var errors = ScrapeAASRole.RequiredRoles
            .Select(role => roles.TryGetValue(role, out _) ? null : new MissingScrapeAASRoleException(role))
            .Where(error => error is not null);

        MissingScrapeAASRoleException.ThrowIfAny(errors!);
    }

    public Action<IScrapeAASConfiguration, IServiceCollection>? GetConfigurationOrDefault(ScrapeAASRole role)
    {
        return _usecases
            .Where(u => !u.Role.IsDefault)
            .FirstOrDefault(u => u.Role == role)
            .ConfigureServices;
    }

    public IEnumerator<ScrapeAASRole> GetEnumerator()
    {
        return _usecases.Select(usecase => usecase.Role).Where(role => !role.IsDefault).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public static class ScrapeAASConfigurationExtensions
{
    public static IServiceCollection AddScrapeAAS(this IServiceCollection services, Action<IScrapeAASConfiguration> configure)
    {
        DefaultScrapeAASConfiguration configuration = new();
        configure(configuration);
        return services;
    }
}

public class MissingScrapeAASRoleException : Exception
{
    public MissingScrapeAASRoleException()
    {
    }

    public MissingScrapeAASRoleException(ScrapeAASRole role) : base(GetErrorMessage(role))
    {
        Role = role;
    }

    public MissingScrapeAASRoleException(ScrapeAASRole role, Exception? innerException) : base(GetErrorMessage(role), innerException)
    {
        Role = role;
    }

    public MissingScrapeAASRoleException(string? message) : base(message)
    {
    }

    public MissingScrapeAASRoleException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    protected MissingScrapeAASRoleException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

    public ScrapeAASRole Role { get; }

    [DoesNotReturn, DebuggerStepThrough, MethodImpl(MethodImplOptions.NoInlining)]
    public static void Throw(ScrapeAASRole role, Exception? innerException = null)
    {
        throw new MissingScrapeAASRoleException(role, innerException);
    }

    [DebuggerStepThrough, MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowIfAny(IEnumerable<MissingScrapeAASRoleException> errors)
    {
        var errorArray = errors.ToArray();
        if (errorArray.Length == 0)
        {
            return;
        }
        if (errorArray.Length == 1)
        {
            throw errorArray[0];
        }
        throw new AggregateException(GetErrorMessage(errorArray.Select(e => e.Role)), errorArray);
    }

    private static string GetErrorMessage(ScrapeAASRole role)
    {
        return $"Missing ScrapeAAS configuration role {role}.";
    }
    private static string GetErrorMessage(IEnumerable<ScrapeAASRole> role)
    {
        return $"Missing ScrapeAAS configuration roles {string.Join(", ", role.Select(role => role.ToString()))}.";
    }
}