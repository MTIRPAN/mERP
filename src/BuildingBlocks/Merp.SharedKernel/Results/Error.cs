namespace Merp.SharedKernel.Results;

/// <summary>
/// Expected business error. <see cref="Code"/> is a localization key (<c>{module}.{area}.{key}</c>, D11) that the
/// API layer turns into a localized ProblemDetails; <see cref="Args"/> fill the message placeholders.
/// </summary>
public sealed record Error
{
    public static readonly IReadOnlyDictionary<string, object?> NoArgs = new Dictionary<string, object?>();

    private Error(string code, ErrorType type, IReadOnlyDictionary<string, object?>? args, IReadOnlyList<FieldError>? fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Type = type;
        Args = args ?? NoArgs;
        Fields = fields ?? [];
    }

    public string Code { get; }

    public ErrorType Type { get; }

    public IReadOnlyDictionary<string, object?> Args { get; }

    /// <summary>Field level errors; only used with <see cref="ErrorType.Validation"/>.</summary>
    public IReadOnlyList<FieldError> Fields { get; }

    public static Error Validation(string code, IReadOnlyDictionary<string, object?>? args = null) =>
        new(code, ErrorType.Validation, args, null);

    /// <summary>Validation error with one or more invalid fields.</summary>
    public static Error Validation(IEnumerable<FieldError> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new(CommonErrors.ValidationCode, ErrorType.Validation, null, [.. fields]);
    }

    public static Error NotFound(string code, IReadOnlyDictionary<string, object?>? args = null) =>
        new(code, ErrorType.NotFound, args, null);

    public static Error Conflict(string code, IReadOnlyDictionary<string, object?>? args = null) =>
        new(code, ErrorType.Conflict, args, null);

    public static Error Forbidden(string code, IReadOnlyDictionary<string, object?>? args = null) =>
        new(code, ErrorType.Forbidden, args, null);

    public static Error Failure(string code, IReadOnlyDictionary<string, object?>? args = null) =>
        new(code, ErrorType.Failure, args, null);

    /// <summary>Wraps this error as a field error, e.g. a value object error attached to its input field.</summary>
    public Error ForField(string field) =>
        Validation([new FieldError(field, Code, Args)]);
}
