using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ScrapeAAS;

internal sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public static SnakeCaseNamingPolicy Instance { get; } = new();

    public override string ConvertName(string name)
    {
        return name.ToSnakeCase();
    }
}

internal static class JsonNamingPolicyExtensions
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? ToSnakeCase(this string? value)
    {
        if (value is null)
        {
            return null;
        }
        return string.Concat(value.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToLower();
    }
}