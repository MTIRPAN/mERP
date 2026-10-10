using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.RegularExpressions;
using Fluid;
using Fluid.Values;
using Merp.Application.Abstractions.Notifications;
using Merp.SharedKernel.Results;

namespace Merp.Infrastructure.Templates;

/// <summary>
/// Liquid templates with Fluid (D14). The model is plain values, dictionaries and lists only: no .NET member is
/// reachable from a template, so administrators can edit templates safely.
/// </summary>
internal sealed partial class FluidTemplateRenderer : ITemplateRenderer
{
    private static readonly FluidParser Parser = new();
    private static readonly TemplateOptions Options = new() { CultureInfo = CultureInfo.InvariantCulture };

    // Encodes what HTML needs (< > & quotes) but keeps letters such as ç, ğ, ı, ş readable
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    public Result Validate(string template)
    {
        var parsed = Parse(template);
        return parsed.IsSuccess ? Result.Success() : Result.Failure(parsed.Error);
    }

    public Result<string> Render(string template, IReadOnlyDictionary<string, object?> model, bool html)
    {
        ArgumentNullException.ThrowIfNull(model);
        var parsed = Parse(template);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var context = new TemplateContext(Options);
        foreach (var (name, value) in model)
        {
            context.SetValue(name, ToFluid(value));
        }

        return parsed.Value.Render(context, html ? Html : NullEncoder.Default);
    }

    private static Result<IFluidTemplate> Parse(string template)
    {
        if (Parser.TryParse(template ?? string.Empty, out var parsed, out var error))
        {
            return Result.Success(parsed);
        }

        // Fluid reports "... at (line:column)"
        var position = Position().Match(error ?? string.Empty);
        var line = position.Success ? int.Parse(position.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
        return Error.Validation(
            TemplateErrors.InvalidCode,
            new Dictionary<string, object?> { ["line"] = line, ["message"] = Position().Replace(error ?? string.Empty, string.Empty).Trim() });
    }

    /// <summary>Dictionaries and lists become Liquid objects and arrays; everything else a plain value.</summary>
    private static FluidValue ToFluid(object? value) => value switch
    {
        null => NilValue.Instance,
        FluidValue fluid => fluid,
        string text => new StringValue(text),
        IReadOnlyDictionary<string, object?> map => FluidValue.Create(map.ToDictionary(p => p.Key, p => (object)ToFluid(p.Value), StringComparer.Ordinal), Options),
        IEnumerable<object?> items => new ArrayValue(items.Select(ToFluid).ToArray()),
        _ => FluidValue.Create(value, Options),
    };

    [GeneratedRegex(@"\s*at \((\d+):(\d+)\)")]
    private static partial Regex Position();
}
