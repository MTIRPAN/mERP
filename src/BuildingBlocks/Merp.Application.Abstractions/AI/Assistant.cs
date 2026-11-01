using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.AI;

/// <summary>
/// A read-only question the assistant can answer (PLAN A12, D65). The model only picks the action and its arguments
/// from the user's words; the action runs as the user through the module's own queries, so permissions and data scope
/// apply, and its answer goes to the user, never to the model. Modules implement it in their application layer; it is
/// registered with the module's handlers.
/// </summary>
public interface IAssistantAction
{
    AssistantActionDefinition Definition { get; }

    Task<Result<AssistantAnswer>> RunAsync(AssistantArguments arguments, CancellationToken cancellationToken);
}

/// <summary>What the model is told about an action.</summary>
/// <param name="Key">Action key, <c>{module}.{action}</c>.</param>
/// <param name="Description">What the action answers, in English, for the model.</param>
/// <param name="Permission">The permission the user needs for the action to be offered; null for every user.</param>
/// <param name="Parameters">The arguments the model may fill; all are optional.</param>
public sealed record AssistantActionDefinition(
    string Key,
    string Description,
    string? Permission,
    IReadOnlyList<AssistantParameter> Parameters);

/// <param name="Name">Argument name, lower snake case.</param>
/// <param name="Type">How the value is given.</param>
/// <param name="Description">What the argument means, in English, for the model.</param>
/// <param name="Choices">For <see cref="AssistantParameterType.Choice"/>: the values allowed.</param>
public sealed record AssistantParameter(string Name, AssistantParameterType Type, string Description, IReadOnlyList<string>? Choices = null);

public enum AssistantParameterType
{
    /// <summary>Free text, at most <see cref="AssistantArguments.TextMaxLength"/> characters.</summary>
    Text,

    /// <summary>One or more of the parameter's choices, separated by commas.</summary>
    Choice,
}

/// <summary>The arguments of a run, already checked against the action's parameters: unknown or invalid ones are left out.</summary>
public sealed class AssistantArguments(IReadOnlyDictionary<string, string> values)
{
    public const int TextMaxLength = 100;

    public static readonly AssistantArguments None = new(new Dictionary<string, string>());

    public string? Text(string name) => values.GetValueOrDefault(name);

    /// <summary>The chosen values of a choice parameter, parsed as <typeparamref name="TEnum"/>; empty when none.</summary>
    public IReadOnlyList<TEnum> Choices<TEnum>(string name)
        where TEnum : struct, Enum =>
        values.GetValueOrDefault(name) is { } value
            ? [.. value.Split(',').Select(v => Enum.TryParse<TEnum>(v, ignoreCase: false, out var parsed) ? parsed : (TEnum?)null).OfType<TEnum>()]
            : [];
}

/// <summary>An action's answer: the first matching rows and a link to the page with all of them.</summary>
/// <param name="Total">How many rows match.</param>
/// <param name="Items">The first rows, at most <see cref="MaxItems"/>.</param>
/// <param name="Link">The web page showing all matches, e.g. <c>/hr/employees?search=ayşe</c>.</param>
public sealed record AssistantAnswer(int Total, IReadOnlyList<AssistantItem> Items, string? Link)
{
    public const int MaxItems = 5;
}

/// <param name="Title">The row's main text, shown as is.</param>
/// <param name="Detail">A secondary line, shown as is.</param>
/// <param name="Link">The web page of the row.</param>
public sealed record AssistantItem(string Title, string? Detail, string? Link);
