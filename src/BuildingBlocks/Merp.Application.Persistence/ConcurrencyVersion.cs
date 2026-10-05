using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Merp.Application.Persistence;

/// <summary>
/// Optimistic concurrency token of every module entity: PostgreSQL's <c>xmin</c>, mapped to a shadow property (D9).
/// Read it in projections with <c>EF.Property&lt;uint&gt;(entity, ConcurrencyVersion.PropertyName)</c> (written inline:
/// EF cannot translate a helper method); set the version the client edited with <see cref="ExpectVersion"/> so a
/// concurrent change ends in 409.
/// </summary>
public static class ConcurrencyVersion
{
    public const string PropertyName = "Version";

    /// <summary>Saves only when the row still has <paramref name="version"/>, otherwise the command fails with 409.</summary>
    public static void ExpectVersion(this EntityEntry entry, uint version)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Property(PropertyName).OriginalValue = version;
    }
}
