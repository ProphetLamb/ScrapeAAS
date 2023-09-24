using System;
using System.Runtime.CompilerServices;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapeAAS;

internal static class MessagePipeExtensions
{
    public static ServiceLifetime ToServiceLifetime(this InstanceLifetime lifetime)
    {
        return Unsafe.As<InstanceLifetime, ServiceLifetime>(ref lifetime);
    }
}
