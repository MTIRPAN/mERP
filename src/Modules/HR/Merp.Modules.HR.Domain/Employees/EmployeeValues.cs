using System.Text.Json.Serialization;
using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;

namespace Merp.Modules.HR.Domain.Employees;

public readonly record struct EmployeeId(Guid Value) : IEntityId<EmployeeId>
{
    public static EmployeeId From(Guid value) => new(value);
}

public readonly record struct EmploymentId(Guid Value) : IEntityId<EmploymentId>
{
    public static EmploymentId From(Guid value) => new(value);
}

public readonly record struct JobAssignmentId(Guid Value) : IEntityId<JobAssignmentId>
{
    public static JobAssignmentId From(Guid value) => new(value);
}

[JsonConverter(typeof(JsonStringEnumConverter<Gender>))]
public enum Gender
{
    Female,
    Male,
}

[JsonConverter(typeof(JsonStringEnumConverter<MaritalStatus>))]
public enum MaritalStatus
{
    Unmarried,
    Married,
    Divorced,
    Widowed,
}

/// <summary>Military service (askerlik durumu).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MilitaryStatus>))]
public enum MilitaryStatus
{
    NotApplicable,
    Completed,
    Exempt,
    Postponed,
    NotCompleted,
}

[JsonConverter(typeof(JsonStringEnumConverter<EmploymentType>))]
public enum EmploymentType
{
    /// <summary>Belirsiz süreli.</summary>
    Indefinite,

    /// <summary>Belirli süreli: has a contract end date.</summary>
    FixedTerm,

    /// <summary>Kısmi süreli: has weekly hours.</summary>
    PartTime,

    Intern,

    Seasonal,
}

/// <summary>State of an employment on a day, from its dates.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmploymentStatus>))]
public enum EmploymentStatus
{
    /// <summary>The hire date is still to come.</summary>
    Upcoming,

    Active,

    /// <summary>After the termination date.</summary>
    Terminated,
}

/// <summary>Where the link of the person to a system user stands (D39); Platform writes the link and reports back.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UserLinkState>))]
public enum UserLinkState
{
    None,

    /// <summary>A request to create, link or unlink a user waits for Platform.</summary>
    Pending,

    Linked,

    /// <summary>Platform refused the last request; the error code says why.</summary>
    Failed,
}

/// <summary>Why an assignment started (değişiklik nedeni).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssignmentReason>))]
public enum AssignmentReason
{
    Hire,
    Transfer,
    Promotion,
    Reorganization,
    Correction,
}

[JsonConverter(typeof(JsonStringEnumConverter<ContactKind>))]
public enum ContactKind
{
    Work,
    Personal,
    Mobile,
    Home,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<FamilyRelation>))]
public enum FamilyRelation
{
    Spouse,
    Child,
    Mother,
    Father,
    Sibling,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<EducationLevel>))]
public enum EducationLevel
{
    Primary,
    Secondary,
    HighSchool,
    Associate,
    Bachelor,
    Master,
    Doctorate,
}

[JsonConverter(typeof(JsonStringEnumConverter<CertificateKind>))]
public enum CertificateKind
{
    Certificate,
    DriverLicense,
}

/// <summary>CEFR level of a language, or native.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LanguageLevel>))]
public enum LanguageLevel
{
    A1,
    A2,
    B1,
    B2,
    C1,
    C2,
    Native,
}

/// <summary>An e-mail address of the employee.</summary>
public sealed record EmployeeEmail(ContactKind Kind, string Address, bool IsPrimary);

/// <summary>A phone number of the employee, normalized.</summary>
public sealed record EmployeePhone(ContactKind Kind, string Number, bool IsPrimary);

/// <summary>An address of the employee; the country code is ISO 3166-1 alpha-2, e.g. TR.</summary>
public sealed record EmployeeAddress(ContactKind Kind, string Line, string? District, string? City, string? PostalCode, string CountryCode);

/// <summary>Whom to call in an emergency.</summary>
public sealed record EmergencyContact(string FullName, FamilyRelation Relation, string Phone);

/// <summary>A family member (aile bilgisi).</summary>
public sealed record FamilyMember(FamilyRelation Relation, string FullName, DateOnly? BirthDate, bool IsWorking);

public sealed record Education(EducationLevel Level, string School, string? Department, int? GraduationYear);

/// <summary>A certificate or a driver's licence (with its class as the name), with an optional validity.</summary>
public sealed record Certificate(CertificateKind Kind, string Name, string? Number, DateOnly? IssuedOn, DateOnly? ValidUntil);

/// <summary>A foreign language; the code is ISO 639-1, e.g. en.</summary>
public sealed record LanguageSkill(string Code, LanguageLevel Level);

/// <summary>Identity details of a person; the nationality is ISO 3166-1 alpha-2, e.g. TR.</summary>
public sealed record PersonProfile(
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string? BirthPlace,
    Gender? Gender,
    string? Nationality,
    MaritalStatus? MaritalStatus,
    MilitaryStatus? MilitaryStatus);

/// <summary>
/// Terms of an employment (istihdam koşulları). Seniority starts on the hire date when no seniority date is given; a
/// different date needs a reason.
/// </summary>
public sealed record EmploymentTerms(
    Guid CompanyId,
    DateOnly HireDate,
    DateOnly? SeniorityDate,
    string? SeniorityReason,
    EmploymentType Type,
    DateOnly? ContractEndDate,
    decimal? WeeklyHours,
    DateOnly? ProbationEndDate,
    Guid? WorkScheduleId,
    Guid? LeavePolicyId);

/// <summary>What an assignment holds; the job, level and cost center are copied from the position and its unit.</summary>
public sealed record AssignmentDetails(
    Organization.PositionId PositionId,
    Guid? BranchId,
    Organization.PositionId? ManagerPositionId,
    Organization.JobId JobId,
    string? Level,
    string? CostCenterCode);

/// <summary>How an employment ended.</summary>
public sealed record TerminationDetails(DateOnly Date, string SgkExitCode, string? Reason, bool Rehireable, string? Note);

/// <summary>
/// The person moved to another company of the group (D38): the source employment ended the day before
/// <paramref name="TransferDate"/>; with <paramref name="TransferLeaveBalance"/> its leave balance moves along (D43).
/// </summary>
public sealed record EmploymentTransferredInGroup(
    EmployeeId EmployeeId,
    EmploymentId SourceEmploymentId,
    EmploymentId TargetEmploymentId,
    Guid SourceCompanyId,
    Guid TargetCompanyId,
    DateOnly TransferDate,
    bool TransferLeaveBalance) : IDomainEvent;
