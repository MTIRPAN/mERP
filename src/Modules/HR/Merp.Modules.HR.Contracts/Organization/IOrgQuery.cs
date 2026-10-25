namespace Merp.Modules.HR.Contracts.Organization;

/// <summary>An employee holding a position.</summary>
/// <param name="EmployeeId">The employee.</param>
/// <param name="UserId">The employee's platform user (D6); null without one.</param>
public sealed record OrgPerson(Guid EmployeeId, Guid? UserId);

/// <summary>The manager position of a unit and who holds it.</summary>
/// <param name="OrgUnitId">The unit the position manages: the asked unit, or the nearest unit above it with a manager.</param>
/// <param name="PositionId">The manager position.</param>
/// <param name="Holders">Its holders today; empty when vacant.</param>
public sealed record OrgUnitManager(Guid OrgUnitId, Guid PositionId, IReadOnlyList<OrgPerson> Holders);

/// <summary>One manager above an employee.</summary>
/// <param name="Depth">1 for the direct manager, 2 for the manager's manager...</param>
/// <param name="PositionId">The manager position.</param>
/// <param name="OrgUnitId">The manager position's unit.</param>
/// <param name="Holders">Its holders today; empty when vacant.</param>
public sealed record ManagerChainEntry(int Depth, Guid PositionId, Guid OrgUnitId, IReadOnlyList<OrgPerson> Holders);

/// <summary>A position held by a user's employee.</summary>
public sealed record UserOrgPosition(Guid PositionId, Guid OrgUnitId, Guid CompanyId, Guid JobId, bool IsPrimary);

/// <summary>Where a user stands in the organization.</summary>
/// <param name="UserId">The platform user.</param>
/// <param name="EmployeeId">The user's employee.</param>
/// <param name="Positions">The primary position first.</param>
public sealed record UserOrgInfo(Guid UserId, Guid EmployeeId, IReadOnlyList<UserOrgPosition> Positions);

/// <summary>A unit of a company, for other modules' pickers.</summary>
public sealed record OrgUnitInfo(Guid OrgUnitId, Guid? ParentId, string Code, string Name);

/// <summary>A position with its seats: Norm seats, Filled taken today.</summary>
/// <remarks>IsActive: open for hiring; a closed position has no vacant seats.</remarks>
public sealed record OrgPositionInfo(
    Guid PositionId,
    string Code,
    Guid CompanyId,
    Guid OrgUnitId,
    string OrgUnitName,
    Guid JobId,
    string JobName,
    int Norm,
    int Filled,
    bool IsActive)
{
    /// <summary>Seats free today.</summary>
    public int Vacant => IsActive ? Math.Max(Norm - Filled, 0) : 0;
}

/// <summary>
/// The organization for other modules (D37), read only. Scopes are not applied: the caller decides what to show.
/// Until job assignments exist (H2) positions have no holders, so the person related answers are empty.
/// </summary>
public interface IOrgQuery
{
    /// <summary>The unit and every unit below it; empty for an unknown unit.</summary>
    Task<IReadOnlyList<Guid>> GetSubtreeUnitIdsAsync(Guid orgUnitId, CancellationToken cancellationToken = default);

    /// <summary>The manager position of a unit, or of the nearest unit above it that has one; null when there is none.</summary>
    Task<OrgUnitManager?> GetUnitManagerAsync(Guid orgUnitId, CancellationToken cancellationToken = default);

    /// <summary>The managers above an employee's primary position, nearest first; empty without a position.</summary>
    Task<IReadOnlyList<ManagerChainEntry>> GetManagerChainAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>The positions of a user's employee; null when the user holds none.</summary>
    Task<UserOrgInfo?> GetUserOrgInfoAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The units of a company valid today, by name (D48).</summary>
    Task<IReadOnlyList<OrgUnitInfo>> GetUnitsAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>The positions of a company, of a unit and the units below it when given, with their seats (D48).</summary>
    Task<IReadOnlyList<OrgPositionInfo>> GetPositionsAsync(Guid companyId, Guid? orgUnitId, CancellationToken cancellationToken = default);

    /// <summary>A position with its seats; null when unknown.</summary>
    Task<OrgPositionInfo?> GetPositionAsync(Guid positionId, CancellationToken cancellationToken = default);
}
