using Merp.Modules.HR.Domain.Organization;

namespace Merp.Modules.HR.Application.Organization;

/// <summary>An employee holding a position today.</summary>
/// <param name="PositionId">The position.</param>
/// <param name="EmployeeId">The employee.</param>
/// <param name="UserId">The employee's platform user (D6); null without one.</param>
/// <param name="IsPrimary">The employee's main position; others are additional duties.</param>
/// <param name="ManagerPositionId">The position the person reports to instead of the position's manager (D38).</param>
public sealed record PositionHolder(PositionId PositionId, Guid EmployeeId, Guid? UserId, bool IsPrimary, PositionId? ManagerPositionId = null);

/// <summary>
/// Who holds which position today (D37): the assignments covering today in their company's time zone (D38).
/// Occupancy, vacancies, the approver resolvers, the ManagedTeam and Self scopes and the person related
/// <c>IOrgQuery</c> methods go through it. Users come with the employee–user link (H3).
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

/// <summary>The managers above a holder: the manager override first, then the position's reporting lines (D38).</summary>
internal static class ManagerChains
{
    public static async Task<IReadOnlyList<PositionId>> OfAsync(PositionHolder holder, IReportingLines reportingLines, CancellationToken cancellationToken)
    {
        if (holder.ManagerPositionId is not { } overridden)
        {
            return await reportingLines.GetManagerChainAsync(holder.PositionId, cancellationToken);
        }

        var above = await reportingLines.GetManagerChainAsync(overridden, cancellationToken);
        return [.. new[] { overridden }.Concat(above).Where(p => p != holder.PositionId).Distinct()];
    }
}
