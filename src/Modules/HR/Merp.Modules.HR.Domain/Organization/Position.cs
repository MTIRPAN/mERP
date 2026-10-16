using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Domain.Organization;

public readonly record struct PositionId(Guid Value) : IEntityId<PositionId>
{
    public static PositionId From(Guid value) => new(value);
}

/// <summary>State of a position (kadro durumu).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PositionStatus>))]
public enum PositionStatus
{
    /// <summary>In use; empty seats are vacancies.</summary>
    Active,

    /// <summary>Kept but not to be filled: empty seats are not vacancies.</summary>
    Frozen,

    /// <summary>No longer used; the reporting lines pass through it to its own manager.</summary>
    Closed,
}

/// <summary>
/// A position (kadro) in a unit: a job with a number of seats (norm). Its manager is the position it reports to;
/// without one, the manager position of its unit, or of the nearest unit above when it manages its own unit (D37).
/// The company and the code never change; the code is unique within the company.
/// </summary>
[Audited]
public sealed class Position : AuditableAggregateRoot<PositionId>, ICompanyScoped
{
    public const int CodeMaxLength = 20;
    public const int MaxNorm = 999;

    private Position()
    {
    }

    /// <summary>The company of the position's unit (Platform's id).</summary>
    public Guid CompanyId { get; private set; }

    public OrgUnitId OrgUnitId { get; private set; }

    public JobId JobId { get; private set; }

    public string Code { get; private set; } = null!;

    /// <summary>The position it reports to, in the same company; null follows the unit's manager.</summary>
    public PositionId? ReportsToPositionId { get; private set; }

    /// <summary>Number of seats (norm kadro).</summary>
    public int Norm { get; private set; }

    public PositionStatus Status { get; private set; }

    /// <summary>A managerial position (yönetici pozisyonu).</summary>
    public bool IsManager { get; private set; }

    public static Position Create(Guid companyId, Code code, PositionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A position belongs to a company.", nameof(companyId));
        }

        if (code.Value.Length > CodeMaxLength)
        {
            throw new ArgumentException($"A position code has at most {CodeMaxLength} characters.", nameof(code));
        }

        var position = new Position { Id = EntityId.New<PositionId>(), CompanyId = companyId, Code = code.Value };
        position.Apply(profile);
        position.Raise(new ReportingStructureChanged(companyId));
        return position;
    }

    /// <summary>Changes the details; the reporting lines follow when the unit, the manager or the status changes.</summary>
    public void Update(PositionProfile profile)
    {
        var before = (OrgUnitId, ReportsToPositionId, Status);
        Apply(profile);
        if (before != (OrgUnitId, ReportsToPositionId, Status))
        {
            Raise(new ReportingStructureChanged(CompanyId));
        }
    }

    /// <summary>Seats that count as vacancies when <paramref name="filled"/> of them are taken.</summary>
    public int VacantSeats(int filled) => Status == PositionStatus.Active ? Math.Max(Norm - filled, 0) : 0;

    private void Apply(PositionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(profile.Status))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile.Status, "Unknown position status.");
        }

        if (profile.Norm is < 1 or > MaxNorm)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile.Norm, $"A position has 1 to {MaxNorm} seats.");
        }

        if (profile.ReportsToPositionId == Id)
        {
            throw new ArgumentException("A position cannot report to itself.", nameof(profile));
        }

        OrgUnitId = profile.OrgUnitId;
        JobId = profile.JobId;
        ReportsToPositionId = profile.ReportsToPositionId;
        Norm = profile.Norm;
        Status = profile.Status;
        IsManager = profile.IsManager;
    }
}

/// <summary>Editable details of a position.</summary>
public sealed record PositionProfile(
    OrgUnitId OrgUnitId,
    JobId JobId,
    PositionId? ReportsToPositionId,
    int Norm,
    PositionStatus Status,
    bool IsManager);

/// <summary>
/// Something that changes who manages whom in a company happened: a position was added or moved, its manager or
/// status changed, a unit moved or got another manager. The company's reporting lines are rebuilt (D37).
/// </summary>
public sealed record ReportingStructureChanged(Guid CompanyId) : IDomainEvent;
