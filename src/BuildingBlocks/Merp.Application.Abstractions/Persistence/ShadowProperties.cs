namespace Merp.Application.Abstractions.Persistence;

/// <summary>EF Core shadow properties the module DbContexts define, for use in queries (<c>EF.Property</c>).</summary>
public static class ShadowProperties
{
    /// <summary>Optimistic concurrency token mapped to PostgreSQL's <c>xmin</c> (D9). DTOs expose it as <c>version</c>.</summary>
    public const string Version = "Version";

    /// <summary>Stored, folded (<c>TurkishText.Fold</c>) search text of a list, matched with <c>Contains</c>.</summary>
    public const string SearchText = "SearchText";
}
