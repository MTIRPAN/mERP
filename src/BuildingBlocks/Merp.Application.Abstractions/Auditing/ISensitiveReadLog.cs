using System.Reflection;
using Merp.SharedKernel.Markers;

namespace Merp.Application.Abstractions.Auditing;

/// <summary>
/// Records that the current user saw the values of <c>[Sensitive]</c> fields (KVKK, D13): one <c>Read</c> audit entry
/// per field, in the module that owns the entity. Call it when a query returns such values unmasked.
/// </summary>
/// <remarks>
/// The entries are written right away and independently of any command transaction: the data has been seen even if
/// the request fails afterwards.
/// </remarks>
public interface ISensitiveReadLog
{
    Task RecordAsync(Type entityType, string entityId, IReadOnlyCollection<string> fields, CancellationToken cancellationToken);
}

public static class SensitiveReadLogExtensions
{
    /// <summary>Records the read of every <c>[Sensitive]</c> property of <typeparamref name="TEntity"/>.</summary>
    public static Task RecordAsync<TEntity>(this ISensitiveReadLog log, string entityId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.RecordAsync(typeof(TEntity), entityId, SensitiveFields.Of(typeof(TEntity)), cancellationToken);
    }
}

public static class SensitiveFields
{
    /// <summary>Names of the properties marked <c>[Sensitive]</c>.</summary>
    public static IReadOnlyList<string> Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(SensitiveAttribute), inherit: true))
            .Select(p => p.Name)];
    }
}
