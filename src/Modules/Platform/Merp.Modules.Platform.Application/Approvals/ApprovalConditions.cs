using Merp.Application.Abstractions.Approvals;
using System.Globalization;
using System.Text.Json;
using Merp.SharedKernel.Results;

namespace Merp.Modules.Platform.Application.Approvals;

/// <summary>A node of the JSON rule tree of §2.3: <c>all</c>, <c>any</c> or a rule on one fact.</summary>
public abstract record ConditionNode;

public sealed record AllCondition(IReadOnlyList<ConditionNode> Items) : ConditionNode;

public sealed record AnyCondition(IReadOnlyList<ConditionNode> Items) : ConditionNode;

public sealed record FactCondition(string Fact, string Op, JsonElement? Value) : ConditionNode;

/// <summary>
/// The condition language of approval flows (§2.3): no code runs, a JSON tree is checked against the document
/// type's facts when a flow is saved and evaluated against a request's facts.
/// </summary>
public static class ApprovalConditions
{
    public const string InvalidCode = "platform.approvals.errors.invalid_condition";
    public const string UnknownFactCode = "platform.approvals.errors.unknown_fact";
    public const string InvalidOperatorCode = "platform.approvals.errors.invalid_operator";
    public const string InvalidValueCode = "platform.approvals.errors.invalid_value";

    private const int MaxDepth = 10;

    private static readonly string[] Comparisons = ["gt", "gte", "lt", "lte"];
    private static readonly string[] Operators = ["eq", "ne", "gt", "gte", "lt", "lte", "in", "nin", "contains", "isEmpty", "isNotEmpty"];

    /// <summary>The tree of a condition, checked against the facts; null when there is none.</summary>
    /// <param name="json">The condition as JSON text.</param>
    /// <param name="facts">Facts of the document type.</param>
    /// <param name="field">Field name for errors, e.g. <c>steps[1].condition</c>.</param>
    public static Result<ConditionNode?> Parse(string? json, IReadOnlyList<ApprovalFact> facts, string field)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result.Success<ConditionNode?>(null);
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return Error.Validation([new FieldError(field, InvalidCode)]);
        }

        var node = Read(root, facts, 0, out var error);
        return error is not null
            ? Error.Validation([new FieldError(field, error.Value.Code, error.Value.Args)])
            : Result.Success<ConditionNode?>(node);
    }

    /// <summary>Whether a request's facts meet the condition; a missing fact counts as empty.</summary>
    public static bool Evaluate(ConditionNode? condition, JsonElement facts) => condition switch
    {
        null => true,
        AllCondition all => all.Items.All(item => Evaluate(item, facts)),
        AnyCondition any => any.Items.Any(item => Evaluate(item, facts)),
        FactCondition rule => Evaluate(rule, facts),
        _ => false,
    };

    private static ConditionNode? Read(JsonElement element, IReadOnlyList<ApprovalFact> facts, int depth, out (string Code, IReadOnlyDictionary<string, object?> Args)? error)
    {
        error = null;
        if (element.ValueKind != JsonValueKind.Object || depth > MaxDepth)
        {
            error = (InvalidCode, Error.NoArgs);
            return null;
        }

        foreach (var group in new[] { "all", "any" })
        {
            if (element.TryGetProperty(group, out var list))
            {
                if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0 || element.EnumerateObject().Count() != 1)
                {
                    error = (InvalidCode, Error.NoArgs);
                    return null;
                }

                var items = new List<ConditionNode>();
                foreach (var item in list.EnumerateArray())
                {
                    var child = Read(item, facts, depth + 1, out error);
                    if (error is not null)
                    {
                        return null;
                    }

                    items.Add(child!);
                }

                return group == "all" ? new AllCondition(items) : new AnyCondition(items);
            }
        }

        if (!element.TryGetProperty("fact", out var factName) || factName.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("op", out var opElement) || opElement.ValueKind != JsonValueKind.String)
        {
            error = (InvalidCode, Error.NoArgs);
            return null;
        }

        var name = factName.GetString()!;
        var op = opElement.GetString()!;
        var args = new Dictionary<string, object?> { ["fact"] = name, ["op"] = op };
        if (facts.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal)) is not { } fact)
        {
            error = (UnknownFactCode, args);
            return null;
        }

        if (!Operators.Contains(op, StringComparer.Ordinal) || !Supports(fact.Type, op))
        {
            error = (InvalidOperatorCode, args);
            return null;
        }

        JsonElement? value = element.TryGetProperty("value", out var given) ? given.Clone() : null;
        if (!ValueFits(fact.Type, op, value))
        {
            error = (InvalidValueCode, args);
            return null;
        }

        return new FactCondition(name, op, value);
    }

    private static bool Supports(ApprovalFactType type, string op) => op switch
    {
        "gt" or "gte" or "lt" or "lte" => type is ApprovalFactType.Number or ApprovalFactType.Date,
        "contains" => type is ApprovalFactType.Text or ApprovalFactType.TextList,
        "in" or "nin" => type != ApprovalFactType.TextList,
        _ => true,
    };

    private static bool ValueFits(ApprovalFactType type, string op, JsonElement? value)
    {
        if (op is "isEmpty" or "isNotEmpty")
        {
            return value is null;
        }

        if (value is not { } given)
        {
            return false;
        }

        if (op is "in" or "nin")
        {
            return given.ValueKind == JsonValueKind.Array && given.GetArrayLength() > 0 && given.EnumerateArray().All(item => Scalar(type, item));
        }

        if (op == "contains")
        {
            return given.ValueKind == JsonValueKind.String;
        }

        return type == ApprovalFactType.TextList ? given.ValueKind == JsonValueKind.String : Scalar(type, given);
    }

    private static bool Scalar(ApprovalFactType type, JsonElement value) => type switch
    {
        ApprovalFactType.Number => value.ValueKind == JsonValueKind.Number,
        ApprovalFactType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        ApprovalFactType.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        ApprovalFactType.Identifier => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
        _ => value.ValueKind == JsonValueKind.String,
    };

    private static bool Evaluate(FactCondition rule, JsonElement facts)
    {
        JsonElement? actual = facts.ValueKind == JsonValueKind.Object && facts.TryGetProperty(rule.Fact, out var found)
            && found.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? found
            : null;

        switch (rule.Op)
        {
            case "isEmpty":
                return IsEmpty(actual);
            case "isNotEmpty":
                return !IsEmpty(actual);
        }

        if (actual is not { } value || rule.Value is not { } expected)
        {
            // A missing fact equals nothing and is in no list
            return rule.Op is "ne" or "nin";
        }

        return rule.Op switch
        {
            "eq" => Equal(value, expected),
            "ne" => !Equal(value, expected),
            "in" => expected.EnumerateArray().Any(item => Equal(value, item)),
            "nin" => !expected.EnumerateArray().Any(item => Equal(value, item)),
            "contains" => value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Any(item => Equal(item, expected))
                : value.ValueKind == JsonValueKind.String && value.GetString()!.Contains(expected.GetString()!, StringComparison.OrdinalIgnoreCase),
            _ when Comparisons.Contains(rule.Op, StringComparer.Ordinal) => Compare(value, expected) is { } order && rule.Op switch
            {
                "gt" => order > 0,
                "gte" => order >= 0,
                "lt" => order < 0,
                _ => order <= 0,
            },
            _ => false,
        };
    }

    private static bool IsEmpty(JsonElement? value) => value is not { } given
        || (given.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(given.GetString()))
        || (given.ValueKind == JsonValueKind.Array && given.GetArrayLength() == 0);

    private static bool Equal(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
        {
            return left.GetDecimal() == right.GetDecimal();
        }

        if (left.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return right.ValueKind == left.ValueKind;
        }

        if (left.ValueKind == JsonValueKind.String && right.ValueKind == JsonValueKind.String)
        {
            var (a, b) = (left.GetString()!, right.GetString()!);
            return Guid.TryParse(a, out var ga) && Guid.TryParse(b, out var gb) ? ga == gb : string.Equals(a, b, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Order of numbers or ISO dates; null when they cannot be compared.</summary>
    private static int? Compare(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
        {
            return left.GetDecimal().CompareTo(right.GetDecimal());
        }

        return left.ValueKind == JsonValueKind.String && right.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(left.GetString(), CultureInfo.InvariantCulture, out var a)
            && DateOnly.TryParse(right.GetString(), CultureInfo.InvariantCulture, out var b)
                ? a.CompareTo(b)
                : null;
    }
}
