using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ScrapeAAS
{
    public static class FactoryHelper
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
    }
}
