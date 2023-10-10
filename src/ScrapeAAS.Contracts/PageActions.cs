using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrapeAAS;

/// <summary>
/// A action to perform on a page.
/// </summary>
public readonly struct PageAction
{
    private readonly PageActionType _type;
    private readonly int _param0Int;
    private readonly string? _param1String;
    private readonly object[]? _param2Array;

    private PageAction(PageActionType type, int param0Int = default, string? param1String = default, object[]? param2Array = default)
    {
        _type = type;
        _param0Int = param0Int;
        _param1String = param1String;
        _param2Array = param2Array;
    }

    public static PageAction Click(string selector)
    {
        return new(PageActionType.Click, param1String: selector);
    }

    public static PageAction Wait(int milliseconds)
    {
        return new(PageActionType.Wait, param0Int: milliseconds);
    }

    public static PageAction ScrollToEnd()
    {
        return new(PageActionType.ScrollToEnd);
    }

    public static PageAction EvaluateExpression(string script)
    {
        return new(PageActionType.EvaluateFunction, param1String: script);
    }

    public static PageAction EvaluateFunction(string pageFunction, params object[] parameters)
    {
        return new(PageActionType.EvaluateFunction, param1String: pageFunction, param2Array: parameters);
    }

    public static PageAction WaitForSelector(string selector)
    {
        return new(PageActionType.WaitForSelector, param1String: selector);
    }

    public static PageAction WaitForNetworkIdle()
    {
        return new(PageActionType.WaitForNetworkIdle);
    }

    public bool TryGetClick([MaybeNullWhen(false)] out string selector)
    {
        selector = _param1String!;
        return _type == PageActionType.Click;
    }

    public bool TryGetWait([MaybeNullWhen(false)] out int milliseconds)
    {
        milliseconds = _param0Int;
        return _type == PageActionType.Wait;
    }

    public bool TryGetEvaluateExpression([MaybeNullWhen(false)] out string script)
    {
        script = _param1String!;
        return _type == PageActionType.EvaluateExpression;
    }

    public bool TryGetEvaluateFunction([MaybeNullWhen(false)] out string pageFunction, [MaybeNullWhen(false)] out object[] parameters)
    {
        pageFunction = _param1String!;
        parameters = _param2Array!;
        return _type == PageActionType.EvaluateFunction;
    }

    public bool TryGetWaitForSelector([MaybeNullWhen(false)] out string selector)
    {
        selector = _param1String!;
        return _type == PageActionType.WaitForSelector;
    }

    public PageActionType Type => _type;
}


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PageActionType
{
    Click,
    Wait,
    ScrollToEnd,
    EvaluateExpression,
    EvaluateFunction,
    WaitForSelector,
    WaitForNetworkIdle
}

public sealed class PageActionJsonConverter : JsonConverter<PageAction>
{
    public override void Write(Utf8JsonWriter writer, PageAction value, JsonSerializerOptions options)
    {
        PageActionJsonDto jsonObject = value.Type switch
        {
            PageActionType.Click when value.TryGetClick(out var selector) => new()
            {
                Type = value.Type,
                Selector = selector,
            },
            PageActionType.Wait when value.TryGetWait(out var milliseconds) => new()
            {
                Type = value.Type,
                Milliseconds = milliseconds,
            },
            PageActionType.EvaluateExpression when value.TryGetEvaluateExpression(out var script) => new()
            {
                Type = value.Type,
                Script = script,
            },
            PageActionType.EvaluateFunction when value.TryGetEvaluateFunction(out var pageFunction, out var parameters) => new()
            {
                Type = value.Type,
                PageFunction = pageFunction,
                Parameters = parameters,
            },
            PageActionType.WaitForSelector when value.TryGetWaitForSelector(out var selector) => new()
            {
                Type = value.Type,
                Selector = selector,
            },
            _ => new() { Type = value.Type }
        };
        JsonSerializer.Serialize(writer, jsonObject, options);
    }
    public override PageAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonObject = JsonSerializer.Deserialize<PageActionJsonDto>(ref reader, options)!;
        return jsonObject.Type switch
        {
            PageActionType.Click => PageAction.Click(jsonObject.Selector ?? throw new InvalidOperationException("Selector is null")),
            PageActionType.Wait => PageAction.Wait(jsonObject.Milliseconds),
            PageActionType.ScrollToEnd => PageAction.ScrollToEnd(),
            PageActionType.EvaluateExpression => PageAction.EvaluateExpression(jsonObject.Script ?? throw new InvalidOperationException("Script is null")),
            PageActionType.EvaluateFunction => PageAction.EvaluateFunction(
                jsonObject.PageFunction ?? throw new InvalidOperationException("PageFunction is null"),
                jsonObject.Parameters ?? throw new InvalidOperationException("Parameters is null")),
            PageActionType.WaitForSelector => PageAction.WaitForSelector(jsonObject.Selector ?? throw new InvalidOperationException("Selector is null")),
            PageActionType.WaitForNetworkIdle => PageAction.WaitForNetworkIdle(),
            _ => throw new NotSupportedException("Unknown PageActionType")
        };
    }

    private sealed class PageActionJsonDto
    {
        [Required]
        public PageActionType Type { get; set; }
        public string? Selector { get; set; }
        public int Milliseconds { get; set; }
        public string? PageFunction { get; set; }
        public object[]? Parameters { get; set; }
        public string? Script { get; set; }
    }
}