using Merp.Modules.HR.Domain.Organization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;

namespace Merp.Modules.HR.Domain.Employees;

/// <summary>
/// An employment period of a person in one company (D6, D38). Its assignments are the job history; the termination
/// fields are set when it ends. Changed only through <see cref="Employee"/>.
/// </summary>
[Audited]
public sealed class Employment : Entity<EmploymentId>
{
    public const int RegistryNumberMaxLength = 30;
    public const int SgkExitCodeMaxLength = 10;
    public const int TextMaxLength = 500;

    private readonly List<JobAssignment> _assignments = [];

    private Employment()
    {
    }

    internal Employment(EmployeeId employeeId, EmploymentTerms terms, string registryNumber, bool isPrimary)
        : base(EntityId.New<EmploymentId>())
    {
        EmployeeId = employeeId;
        CompanyId = terms.CompanyId;
        RegistryNumber = Text.Required(registryNumber, RegistryNumberMaxLength, nameof(registryNumber));
        HireDate = terms.HireDate;
        IsPrimary = isPrimary;
        SeniorityDate = terms.SeniorityDate ?? terms.HireDate;
        SeniorityReason = Text.Optional(terms.SeniorityReason, TextMaxLength, nameof(terms.SeniorityReason));
        ApplyTerms(terms);
    }

    public EmployeeId EmployeeId { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Sicil no, numbered per company (P5, <c>hr.employee</c>).</summary>
    public string RegistryNumber { get; private set; } = null!;

    public DateOnly HireDate { get; private set; }

    /// <summary>Start of seniority (kıdem başlangıcı); before the hire date when earlier service counts.</summary>
    public DateOnly SeniorityDate { get; private set; }

    public string? SeniorityReason { get; private set; }

    public EmploymentType Type { get; private set; }

    /// <summary>End of a fixed term contract.</summary>
    public DateOnly? ContractEndDate { get; private set; }

    /// <summary>Weekly hours of a part time employment.</summary>
    public decimal? WeeklyHours { get; private set; }

    public DateOnly? ProbationEndDate { get; private set; }

    /// <summary>Overrides the work schedule of the branch and company (P9).</summary>
    public Guid? WorkScheduleId { get; private set; }

    /// <summary>Overrides the leave policy (L1).</summary>
    public Guid? LeavePolicyId { get; private set; }

    /// <summary>The employment whose position is the person's primary one.</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>Last working day; null while the employment lasts.</summary>
    public DateOnly? TerminationDate { get; private set; }

    public string? SgkExitCode { get; private set; }

    public string? TerminationReason { get; private set; }

    public bool? Rehireable { get; private set; }

    public string? TerminationNote { get; private set; }

    public IReadOnlyList<JobAssignment> Assignments => _assignments.AsReadOnly();

    public bool IsOpen => TerminationDate is null;

    public EmploymentStatus StatusOn(DateOnly day) =>
        day < HireDate ? EmploymentStatus.Upcoming
        : TerminationDate is { } end && end < day ? EmploymentStatus.Terminated
        : EmploymentStatus.Active;

    /// <summary>The assignment that covers <paramref name="day"/>, if any.</summary>
    public JobAssignment? AssignmentOn(DateOnly day) => _assignments.SingleOrDefault(a => a.Covers(day));

    /// <summary>The latest assignment, which may start in the future.</summary>
    public JobAssignment? LatestAssignment => _assignments.MaxBy(a => a.ValidFrom);

    internal void ApplyTerms(EmploymentTerms terms)
    {
        if (!Enum.IsDefined(terms.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(terms), terms.Type, "Unknown employment type.");
        }

        Type = terms.Type;
        ContractEndDate = terms.Type == EmploymentType.FixedTerm ? terms.ContractEndDate : null;
        WeeklyHours = terms.Type == EmploymentType.PartTime ? terms.WeeklyHours : null;
        ProbationEndDate = terms.ProbationEndDate;
        WorkScheduleId = terms.WorkScheduleId;
        LeavePolicyId = terms.LeavePolicyId;
    }

    internal JobAssignment AddAssignment(AssignmentDetails details, DateOnly validFrom, AssignmentReason reason)
    {
        var assignment = new JobAssignment(Id, details, validFrom, reason);
        _assignments.Add(assignment);
        return assignment;
    }

    internal void RemoveAssignment(JobAssignment assignment) => _assignments.Remove(assignment);

    internal void End(TerminationDetails details)
    {
        TerminationDate = details.Date;
        SgkExitCode = Text.Required(details.SgkExitCode, SgkExitCodeMaxLength, nameof(details.SgkExitCode));
        TerminationReason = Text.Optional(details.Reason, TextMaxLength, nameof(details.Reason));
        Rehireable = details.Rehireable;
        TerminationNote = Text.Optional(details.Note, TextMaxLength, nameof(details.Note));
        IsPrimary = false;
    }

    internal void MakePrimary(bool primary) => IsPrimary = primary;
}

/// <summary>
/// A period in a position (görev ataması); both days inclusive, the end open while it lasts (D38). The unit comes
/// from the position; job, level and cost center are copied so the history does not change with the position.
/// </summary>
[Audited]
public sealed class JobAssignment : Entity<JobAssignmentId>
{
    public const int LevelMaxLength = 10;
    public const int CostCenterCodeMaxLength = 30;

    private JobAssignment()
    {
    }

    internal JobAssignment(EmploymentId employmentId, AssignmentDetails details, DateOnly validFrom, AssignmentReason reason)
        : base(EntityId.New<JobAssignmentId>())
    {
        EmploymentId = employmentId;
        ValidFrom = validFrom;
        Apply(details, reason);
    }

    public EmploymentId EmploymentId { get; private set; }

    public PositionId PositionId { get; private set; }

    public Guid? BranchId { get; private set; }

    /// <summary>The person reports to this position instead of the position's manager (yönetici ezmesi).</summary>
    public PositionId? ManagerPositionId { get; private set; }

    public JobId JobId { get; private set; }

    public string? Level { get; private set; }

    public string? CostCenterCode { get; private set; }

    public AssignmentReason Reason { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    /// <summary>The change was announced to other modules (<c>EmployeeAssignmentChanged</c>); see the nightly job.</summary>
    public bool Announced { get; private set; }

    public bool Covers(DateOnly day) => ValidFrom <= day && (ValidTo is null || day <= ValidTo);

    internal void Apply(AssignmentDetails details, AssignmentReason reason)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown assignment reason.");
        }

        if (details.ManagerPositionId == details.PositionId)
        {
            throw new ArgumentException("A person does not report to their own position.", nameof(details));
        }

        PositionId = details.PositionId;
        BranchId = details.BranchId;
        ManagerPositionId = details.ManagerPositionId;
        JobId = details.JobId;
        Level = Text.Optional(details.Level, LevelMaxLength, nameof(details.Level));
        CostCenterCode = Text.Optional(details.CostCenterCode, CostCenterCodeMaxLength, nameof(details.CostCenterCode));
        Reason = reason;
        Announced = false;
    }

    internal void EndOn(DateOnly day) => ValidTo = day;

    internal void Reopen() => ValidTo = null;

    internal void MarkAnnounced() => Announced = true;
}
