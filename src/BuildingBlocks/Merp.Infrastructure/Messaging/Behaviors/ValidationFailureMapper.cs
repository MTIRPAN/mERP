using System.Text;
using FluentValidation.Results;
using Merp.SharedKernel.Results;

namespace Merp.Infrastructure.Messaging.Behaviors;

/// <summary>
/// Converts FluentValidation failures to <see cref="FieldError"/>s. Error codes containing a dot are used as
/// localization keys as-is; built-in FluentValidation codes are mapped to <c>common.errors.*</c> keys.
/// Property values are never copied into the arguments (they may contain personal data).
/// </summary>
internal static class ValidationFailureMapper
{
    private static readonly Dictionary<string, string> BuiltInCodes = new(StringComparer.Ordinal)
    {
        ["NotEmptyValidator"] = "common.errors.required",
        ["NotNullValidator"] = "common.errors.required",
        ["MaximumLengthValidator"] = "common.errors.max_length",
        ["MinimumLengthValidator"] = "common.errors.min_length",
        ["LengthValidator"] = "common.errors.length",
        ["ExactLengthValidator"] = "common.errors.exact_length",
        ["EmailValidator"] = "common.errors.invalid_email",
        ["GreaterThanValidator"] = "common.errors.greater_than",
        ["GreaterThanOrEqualValidator"] = "common.errors.greater_than_or_equal",
        ["LessThanValidator"] = "common.errors.less_than",
        ["LessThanOrEqualValidator"] = "common.errors.less_than_or_equal",
        ["InclusiveBetweenValidator"] = "common.errors.between",
        ["EnumValidator"] = "common.errors.invalid_value",
        ["RegularExpressionValidator"] = "common.errors.invalid_format",
    };

    private static readonly HashSet<string> ExcludedPlaceholders = new(StringComparer.Ordinal)
    {
        "PropertyName", "PropertyValue", "PropertyPath",
    };

    public static FieldError ToFieldError(ValidationFailure failure)
    {
        var code = failure.ErrorCode switch
        {
            { } c when c.Contains('.', StringComparison.Ordinal) => c,
            { } c when BuiltInCodes.TryGetValue(c, out var mapped) => mapped,
            _ => "common.errors.invalid_value",
        };

        var args = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (failure.FormattedMessagePlaceholderValues is { } placeholders)
        {
            foreach (var (key, value) in placeholders)
            {
                if (!ExcludedPlaceholders.Contains(key))
                {
                    args[CamelCase(key)] = value;
                }
            }
        }

        return new FieldError(CamelCasePath(failure.PropertyName), code, args);
    }

    // "Address.City" -> "address.city", "Items[0].Name" -> "items[0].name"
    private static string CamelCasePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var builder = new StringBuilder(path.Length);
        var startOfSegment = true;
        foreach (var c in path)
        {
            builder.Append(startOfSegment ? char.ToLowerInvariant(c) : c);
            startOfSegment = c == '.';
        }

        return builder.ToString();
    }

    private static string CamelCase(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
