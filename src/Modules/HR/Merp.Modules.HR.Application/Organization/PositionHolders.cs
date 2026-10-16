using Merp.Modules.HR.Domain.Organization;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>An employee holding a position today.</summary>
/// <param name="PositionId">The position.</param>
/// <param name="EmployeeId">The employee.</param>
/// <param name="UserId">The employee's platform user (D6); null without one.</param>
/// <param name="IsPrimary">The employee's main position; others are additional duties.</param>
public sealed record PositionHolder(PositionId PositionId, Guid EmployeeId, Guid? UserId, bool IsPrimary);

/// <summary>
/// Who holds which position today (D37). Occupancy, vacancies, the approver resolvers, the ManagedTeam and Self
/// scopes and the person related <c>IOrgQuery</c> methods go through it. H1 has no job assignments yet, so
/// <see cref="NoPositionHolders"/> finds nobody; H2 fills it from job assignments and H3 adds the users.
/// </summary>
public interface IPositionHolders
{
    /// <summary>The holders of <paramref name="positionIds"/>.</summary>
    Task<IReadOnlyList<PositionHolder>> GetHoldersAsync(IReadOnlyCollection<PositionId> positionIds, CancellationToken cancellationToken = default);

    /// <summary>The positions of an employee, the primary one first.</summary>
    Task<IReadOnlyList<PositionHolder>> GetPositionsOfEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>The positions of a user's employee, the primary one first.</summary>
    Task<IReadOnlyList<PositionHolder>> GetPositionsOfUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Until job assignments exist (H2) every position is empty (D37).</summary>
public sealed class NoPositionHolders : IPositionHolders
{
    public Task<IReadOnlyList<PositionHolder>> GetHoldersAsync(IReadOnlyCollection<PositionId> positionIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PositionHolder>>([]);

    public Task<IReadOnlyList<PositionHolder>> GetPositionsOfEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PositionHolder>>([]);

    public Task<IReadOnlyList<PositionHolder>> GetPositionsOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PositionHolder>>([]);
}

internal static class PositionHolderExtensions
{
    /// <summary>Taken seats per position.</summary>
    public static async Task<Dictionary<PositionId, int>> CountFilledAsync(
        this IPositionHolders holders,
        IReadOnlyCollection<PositionId> positionIds,
        CancellationToken cancellationToken) =>
        positionIds.Count == 0
            ? []
            : (await holders.GetHoldersAsync(positionIds, cancellationToken))
                .GroupBy(h => h.PositionId)
                .ToDictionary(g => g.Key, g => g.Select(h => h.EmployeeId).Distinct().Count());

    /// <summary>The primary position of the requester: by employee when known, otherwise by user.</summary>
    public static async Task<PositionHolder?> FindPrimaryAsync(
        this IPositionHolders holders,
        Guid? employeeId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PositionHolder> positions = employeeId is { } employee
            ? await holders.GetPositionsOfEmployeeAsync(employee, cancellationToken)
            : userId is { } user ? await holders.GetPositionsOfUserAsync(user, cancellationToken) : [];
        return positions.FirstOrDefault(p => p.IsPrimary) ?? (positions.Count > 0 ? positions[0] : null);
    }
}
