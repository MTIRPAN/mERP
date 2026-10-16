using System.Globalization;
using System.Text.Json;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merp.Modules.HR.Application.Approvals;

/// <summary>
/// The requester's manager at a level (<c>line-manager</c>, docs/domain/yetki-ve-onay.md §2.4, D37). Levels count
/// people: going up the reporting lines from the requester's primary position, every manager position that has a
/// holder is one level; vacant positions are skipped. A manager without a platform user finds nobody, with a
/// warning.
/// </summary>
public sealed partial class LineManagerApproverResolver(
    IPositionHolders holders,
    IReportingLines reportingLines,
    ILogger<LineManagerApproverResolver> logger) : IApproverResolver
{
    public const string ResolverKey = "line-manager";
    public const string LevelParameter = "level";

    public string Key => ResolverKey;

    public string NameKey => "hr.approvals.resolvers.line_manager";

    public IReadOnlyList<ApproverParameter> Parameters { get; } = [new(LevelParameter, "hr.approvals.resolvers.line_manager.level")];

    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        ApproverResolutionContext context,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue(LevelParameter, out var value)
            || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            || level < 1)
        {
            return [];
        }

        if (await holders.FindPrimaryAsync(context.RequesterEmployeeId, context.RequesterUserId, cancellationToken) is not { } requester)
        {
            return [];
        }

        var chain = await reportingLines.GetManagerChainAsync(requester.PositionId, cancellationToken);
        if (chain.Count == 0)
        {
            return [];
        }

        var managers = (await holders.GetHoldersAsync(chain, cancellationToken)).Where(h => h.EmployeeId != requester.EmployeeId).ToList();
        var levels = chain
            .Select(position => managers.Where(h => h.PositionId == position).ToList())
            .Where(people => people.Count > 0)
            .ToList();
        if (levels.Count < level)
        {
            return [];
        }

        var users = levels[level - 1].Select(h => h.UserId).OfType<Guid>().Where(u => u != context.RequesterUserId).Distinct().ToList();
        if (users.Count == 0)
        {
            LogManagerWithoutUser(logger, level, context.DocumentType);
        }

        return users;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The level {Level} manager found for a {DocumentType} request has no platform user")]
    private static partial void LogManagerWithoutUser(ILogger logger, int level, string documentType);
}

/// <summary>
/// The manager of a unit (<c>org-unit-manager</c>, §2.4, D37): the unit in the fact named by <c>factName</c>, otherwise
/// the requester's unit. A unit without a manager position is managed by the nearest unit above it; a requester who
/// manages their own unit goes to the manager above.
/// </summary>
public sealed partial class OrgUnitManagerApproverResolver(
    IHrDbContext db,
    IPositionHolders holders,
    ILogger<OrgUnitManagerApproverResolver> logger) : IApproverResolver
{
    public const string ResolverKey = "org-unit-manager";
    public const string FactNameParameter = "factName";

    public string Key => ResolverKey;

    public string NameKey => "hr.approvals.resolvers.org_unit_manager";

    public IReadOnlyList<ApproverParameter> Parameters { get; } =
        [new(FactNameParameter, "hr.approvals.resolvers.org_unit_manager.fact_name", Required: false)];

    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        ApproverResolutionContext context,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);

        OrgUnitId unitId;
        PositionId? skip = null;
        Guid? requesterEmployee = null;
        if (parameters.TryGetValue(FactNameParameter, out var fact) && !string.IsNullOrWhiteSpace(fact))
        {
            if (FactGuid(context.Facts, fact) is not { } factUnit)
            {
                return [];
            }

            unitId = OrgUnitId.From(factUnit);
        }
        else
        {
            if (await holders.FindPrimaryAsync(context.RequesterEmployeeId, context.RequesterUserId, cancellationToken) is not { } requester)
            {
                return [];
            }

            var requesterUnit = await db.Positions.AsNoTracking()
                .Where(p => p.Id == requester.PositionId)
                .Select(p => (OrgUnitId?)p.OrgUnitId)
                .SingleOrDefaultAsync(cancellationToken);
            if (requesterUnit is null)
            {
                return [];
            }

            unitId = requesterUnit.Value;
            skip = requester.PositionId;
            requesterEmployee = requester.EmployeeId;
        }

        if (await UnitManagers.FindAsync(db, unitId, skip, cancellationToken) is not { } manager)
        {
            return [];
        }

        var people = (await holders.GetHoldersAsync([manager.PositionId], cancellationToken)).Where(h => h.EmployeeId != requesterEmployee).ToList();
        var users = people.Select(h => h.UserId).OfType<Guid>().Where(u => u != context.RequesterUserId).Distinct().ToList();
        if (people.Count > 0 && users.Count == 0)
        {
            LogManagerWithoutUser(logger, context.DocumentType);
        }

        return users;
    }

    private static Guid? FactGuid(JsonElement facts, string name) =>
        facts.ValueKind == JsonValueKind.Object
        && facts.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var id)
            ? id
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The unit manager found for a {DocumentType} request has no platform user")]
    private static partial void LogManagerWithoutUser(ILogger logger, string documentType);
}

/// <summary>The holders of a position (<c>position</c>, §2.4, D37).</summary>
public sealed class PositionApproverResolver(IPositionHolders holders) : IApproverResolver
{
    public const string ResolverKey = "position";
    public const string PositionIdParameter = "positionId";

    public string Key => ResolverKey;

    public string NameKey => "hr.approvals.resolvers.position";

    public IReadOnlyList<ApproverParameter> Parameters { get; } = [new(PositionIdParameter, "hr.approvals.resolvers.position.position_id")];

    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        ApproverResolutionContext context,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue(PositionIdParameter, out var value) || !Guid.TryParse(value, out var positionId))
        {
            return [];
        }

        return [.. (await holders.GetHoldersAsync([PositionId.From(positionId)], cancellationToken))
            .Select(h => h.UserId)
            .OfType<Guid>()
            .Distinct()];
    }
}

/// <summary>
/// The manager of an approver, for escalating overdue tasks (§2.5): the nearest manager above the approver's primary
/// position who has a platform user (D37).
/// </summary>
public sealed class HrApproverManagerLookup(IPositionHolders holders, IReportingLines reportingLines) : IApproverManagerLookup
{
    public async Task<Guid?> FindManagerUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (await holders.FindPrimaryAsync(null, userId, cancellationToken) is not { } own)
        {
            return null;
        }

        var chain = await reportingLines.GetManagerChainAsync(own.PositionId, cancellationToken);
        if (chain.Count == 0)
        {
            return null;
        }

        var managers = await holders.GetHoldersAsync(chain, cancellationToken);
        return chain
            .SelectMany(position => managers.Where(h => h.PositionId == position && h.EmployeeId != own.EmployeeId))
            .Select(h => h.UserId)
            .FirstOrDefault(u => u is not null && u != userId);
    }
}
