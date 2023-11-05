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

    IScrapeAASConfiguration Use(ScrapeAASUsecase usecase);

    IScrapeAASConfiguration Use(ScrapeAASRole role, Action<IScrapeAASConfiguration, IServiceCollection> configureServices)
    {
        return Use(new(role, configureServices));
    }

    IScrapeAASConfiguration Use(ScrapeAASRole role, Action<IServiceCollection> configureServices)
    {
        return Use(new(role, (config, services) => configureServices(services)));
    }

    IScrapeAASConfiguration Add(Action<IScrapeAASConfiguration, IServiceCollection> configureServices)
    {
        return Use(new(ScrapeAASRole.Default, configureServices));
    }

    Action<IScrapeAASConfiguration, IServiceCollection>? GetConfigurationOrDefault(ScrapeAASRole role);

    Action<IScrapeAASConfiguration, IServiceCollection> GetConfiguration(ScrapeAASRole role)
    {
        return GetConfigurationOrDefault(role) ?? throw new KeyNotFoundException($"The role {role} does not exist in the configuration");
    }

    void Build(IServiceCollection services);
}

[DebuggerDisplay("Value,nq")]
public sealed class ScrapeAASRole(string value) : IEquatable<ScrapeAASRole>
{
    public string Value { get; } = value;
    public bool IsDefault => string.IsNullOrEmpty(Value);

    public static ScrapeAASRole Default { get; } = new("");
    public static ScrapeAASRole CookieStorage { get; } = new("cookiestorage");
    public static ScrapeAASRole Dataflow { get; } = new("dataflow");
    public static ScrapeAASRole StaticPageLoader { get; } = new("pageloader-static");
    public static ScrapeAASRole BrowserPageLoader { get; } = new("pageloader-browser");
    public static ScrapeAASRole ProxyProvider { get; } = new("proxyprovider");

    public static ImmutableArray<ScrapeAASRole> RequiredRoles { get; } = ImmutableArray.Create([
        CookieStorage,
        Dataflow,
        StaticPageLoader,
        BrowserPageLoader,
    ]);

    public override string ToString()
    {
        return Value;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is ScrapeAASRole other && Equals(other);
    }

    public bool Equals(ScrapeAASRole? other)
    {
        return Value == other?.Value;
    }

    public override int GetHashCode()
    {
        return Value?.GetHashCode() ?? 0;
    }

    public static bool operator ==(ScrapeAASRole lhs, ScrapeAASRole rhs) => lhs.Equals(rhs);
    public static bool operator !=(ScrapeAASRole lhs, ScrapeAASRole rhs) => !lhs.Equals(rhs);
}

public readonly record struct ScrapeAASUsecase(ScrapeAASRole Role, Action<IScrapeAASConfiguration, IServiceCollection> ConfigureServices);

internal sealed class DefaultScrapeAASConfiguration : IScrapeAASConfiguration
{
    private readonly List<ScrapeAASUsecase> _usecases = [];
    private bool _readonly;

    public ServiceLifetime LongLivingServiceLifetime { get; set; } = ServiceLifetime.Scoped;

    public IScrapeAASConfiguration WithLongLivingServiceLifetime(ServiceLifetime lifetime)
    {
        ThrowIfReadonly();
        if (lifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), $"The long living service lifetime can only be {nameof(ServiceLifetime.Scoped)} or {nameof(ServiceLifetime.Singleton)}.");
        }
        return this;
    }

    public IScrapeAASConfiguration Use(ScrapeAASUsecase usecase)
    {
        ThrowIfReadonly();
        if (usecase.Role.IsDefault)
        {
            _usecases.Add(usecase);
            return this;
        }
        var existing = _usecases.FindIndex(u => u.Role == usecase.Role);
        if (existing >= 0)
        {
            _usecases[existing] = usecase;
            return this;
        }

        _usecases.Add(usecase);
        return this;
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
            .FirstOrDefault(u => u.Role == role)
            .ConfigureServices;
    }

    public IEnumerator<ScrapeAASRole> GetEnumerator()
    {
        return _usecases.Select(usecase => usecase.Role).GetEnumerator();
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
        configuration.Build(services);
        return services;
    }
}

public class MissingScrapeAASRoleException : Exception
{
    public MissingScrapeAASRoleException()
    {
        Role = ScrapeAASRole.Default;
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
        Role = ScrapeAASRole.Default;
    }

    public MissingScrapeAASRoleException(string? message, Exception? innerException) : base(message, innerException)
    {
        Role = ScrapeAASRole.Default;
    }

    protected MissingScrapeAASRoleException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        Role = ScrapeAASRole.Default;
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
