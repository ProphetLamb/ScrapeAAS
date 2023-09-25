using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

internal static class FactoryHelper
{
    public static Func<IServiceProvider, object> ConvertImplementationTypeUnsafe(Func<IServiceProvider, object> factory, Type implementationType)
    {
        var method = typeof(FactoryHelper).GetMethod(nameof(FactoryHelper.CastFactoryType), BindingFlags.Static | BindingFlags.NonPublic);
        var genericMethod = method!.MakeGenericMethod(implementationType);
        return (Func<IServiceProvider, object>)genericMethod.Invoke(null, new object[] { factory })!;
    }

    private static Func<IServiceProvider, T> CastFactoryType<T>(Func<IServiceProvider, object> factory) where T : class
    {
        return provider => Unsafe.As<T>(factory(provider));
    }

    public static Type GetImplementationType(this ServiceDescriptor s)
    {
        return s.ImplementationType ?? s.ImplementationInstance?.GetType() ?? s.ImplementationFactory?.GetType().GenericTypeArguments.LastOrDefault()!;
    }


    public static object? GetServiceOfType(this IServiceProvider sp, Type existingServiceType, Type existingImplementationType)
    {
        return sp.GetServices(existingServiceType).FirstOrDefault(x => x is not null && x.GetType() == existingImplementationType);
    }
}
