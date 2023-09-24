using System;
using System.Runtime.CompilerServices;

namespace RedditDotnetScraper;

public static class StringExtensions
{
    public static string AssertNotEmpty(this string? source, [CallerArgumentExpression(nameof(source))] string paramName = "")
    {
        if (string.IsNullOrEmpty(source))
        {
            throw new ArgumentException("Value cannot be null or empty.", paramName);
        }

        return source;
    }
}
