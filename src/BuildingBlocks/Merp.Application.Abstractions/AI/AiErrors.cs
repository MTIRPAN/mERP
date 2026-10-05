using Merp.SharedKernel.Results;

namespace Merp.Application.Abstractions.AI;

/// <summary>Errors returned by <see cref="IAiService"/>. Texts live in the shared <c>tr-TR.json</c>.</summary>
public static class AiErrors
{
    /// <summary>No provider is configured, or the feature is not enabled for the tenant.</summary>
    public static readonly Error Disabled = Error.Forbidden("common.errors.ai_disabled");

    /// <summary>The tenant's monthly AI budget is used up.</summary>
    public static readonly Error BudgetExceeded = Error.Forbidden("common.errors.ai_budget_exceeded");

    /// <summary>The provider could not be reached, timed out or returned an error.</summary>
    public static readonly Error Unavailable = Error.Unavailable("common.errors.ai_unavailable");

    /// <summary>The model declined to process the content.</summary>
    public static readonly Error Refused = Error.Validation("common.errors.ai_refused");

    /// <summary>The output was cut off or could not be read.</summary>
    public static readonly Error InvalidOutput = Error.Unavailable("common.errors.ai_invalid_output");
}
