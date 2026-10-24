using System.Text.Json;
using Merp.SharedKernel.Results;

namespace Merp.Modules.HR.Domain.Leave;

/// <summary>A version of a rule set, in force from its first day until the next version's (D41).</summary>
public interface IVersioned
{
    /// <summary>First day in force; <see cref="DateOnly.MinValue"/> for "from the beginning".</summary>
    DateOnly ValidFrom { get; }
}

/// <summary>
/// The versions of leave types and policies (D41). Versions that have started are history: results computed with
/// them must stay explainable, so they never change. Versions still to come may be changed or removed.
/// </summary>
public static class Versions
{
    public const string LockedCode = "hr.leave.errors.started_version_locked";
    public const string DuplicateCode = "hr.leave.errors.duplicate_version_date";
    public const string RequiredCode = "hr.leave.errors.version_required";

    /// <summary>The version in force on <paramref name="day"/>, or null before the first one.</summary>
    public static T? On<T>(IReadOnlyList<T> versions, DateOnly day)
        where T : class, IVersioned =>
        versions.Where(v => v.ValidFrom <= day).MaxBy(v => v.ValidFrom);

    /// <summary>The versions of a new record, sorted: at least one, each first day once.</summary>
    public static Result<IReadOnlyList<T>> Initial<T>(IReadOnlyList<T> versions)
        where T : class, IVersioned
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (versions.Count == 0)
        {
            return Error.Validation([new FieldError("versions", RequiredCode)]);
        }

        if (versions.Select(v => v.ValidFrom).Distinct().Count() != versions.Count)
        {
            return Error.Validation([new FieldError("versions", DuplicateCode)]);
        }

        return versions.OrderBy(v => v.ValidFrom).ToList();
    }

    /// <summary>
    /// <paramref name="proposed"/> replacing <paramref name="current"/> on <paramref name="today"/>: every version that
    /// has started stays as it is, and no version may start on or before today unless it had already.
    /// </summary>
    public static Result<IReadOnlyList<T>> Replace<T>(IReadOnlyList<T> current, IReadOnlyList<T> proposed, DateOnly today)
        where T : class, IVersioned
    {
        ArgumentNullException.ThrowIfNull(current);
        var initial = Initial(proposed);
        if (initial.IsFailure)
        {
            return initial;
        }

        var started = current.Where(v => v.ValidFrom <= today).ToList();
        var startedProposed = initial.Value.Where(v => v.ValidFrom <= today).ToList();
        var unchanged = started.Count == startedProposed.Count && started.OrderBy(v => v.ValidFrom).Zip(startedProposed).All(pair => Same(pair.First, pair.Second));
        return unchanged ? initial : Error.Validation([new FieldError("versions", LockedCode)]);
    }

    /// <summary>Equal by value, lists included: the versions are compared as they are stored.</summary>
    private static bool Same<T>(T first, T second) =>
        string.Equals(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second), StringComparison.Ordinal);
}
