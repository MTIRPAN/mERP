using Merp.SharedKernel.Domain;
using Merp.SharedKernel.Identity;
using Merp.SharedKernel.Markers;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Domain.Employees;

/// <summary>
/// A person of the tenant (personel, D6, D38): identity, contacts, family, qualifications and custom fields, with
/// their employments and job history. The TCKN never changes and is unique in the tenant. Hiring, assignment
/// changes, termination, rehiring and group transfers all change this one aggregate (CLAUDE.md rule 7).
/// </summary>
[Audited]
public sealed class Employee : AuditableAggregateRoot<EmployeeId>, IHasCustomFields
{
    public const int NameMaxLength = 100;
    public const int PlaceMaxLength = 100;
    public const int TextMaxLength = 200;
    public const int MaxListItems = 50;

    private readonly List<Employment> _employments = [];

    private Employee()
    {
    }

    /// <summary>T.C. kimlik no.</summary>
    [Sensitive]
    public string Tckn { get; private set; } = null!;

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public DateOnly? BirthDate { get; private set; }

    public string? BirthPlace { get; private set; }

    public Gender? Gender { get; private set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? Nationality { get; private set; }

    public MaritalStatus? MaritalStatus { get; private set; }

    public MilitaryStatus? MilitaryStatus { get; private set; }

    [Sensitive]
    public string? Iban { get; private set; }

    /// <summary>The person's platform user (D6); Platform writes the link and HR keeps it from its events (D39).</summary>
    public Guid? UserId { get; private set; }

    public UserLinkState UserLinkState { get; private set; }

    /// <summary>Why Platform refused the last link request, a localization key.</summary>
    public string? UserLinkError { get; private set; }

    /// <summary>When HR asked Platform to deactivate the user because no employment remains (D39).</summary>
    public DateTimeOffset? UserDeactivationRequestedAt { get; private set; }

    public IReadOnlyList<EmployeeEmail> Emails { get; private set; } = [];

    public IReadOnlyList<EmployeePhone> Phones { get; private set; } = [];

    public IReadOnlyList<EmployeeAddress> Addresses { get; private set; } = [];

    public IReadOnlyList<EmergencyContact> EmergencyContacts { get; private set; } = [];

    public IReadOnlyList<FamilyMember> FamilyMembers { get; private set; } = [];

    public IReadOnlyList<Education> Educations { get; private set; } = [];

    public IReadOnlyList<Certificate> Certificates { get; private set; } = [];

    public IReadOnlyList<LanguageSkill> Languages { get; private set; } = [];

    public CustomFieldValues CustomFields { get; private set; } = CustomFieldValues.Empty;

    public IReadOnlyList<Employment> Employments => _employments.AsReadOnly();

    public string FullName => $"{FirstName} {LastName}";

    /// <summary>Hires a new person: the first employment and its first assignment, starting on the hire date.</summary>
    public static Result<Employee> Hire(Tckn tckn, PersonProfile person, EmploymentTerms terms, AssignmentDetails assignment, string registryNumber)
    {
        ArgumentNullException.ThrowIfNull(tckn);
        ArgumentNullException.ThrowIfNull(terms);
        var check = EmploymentRules.Check(terms, requireSeniorityReason: true);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var employee = new Employee { Id = EntityId.New<EmployeeId>(), Tckn = tckn.Value };
        employee.ApplyPerson(person);
        var employment = new Employment(employee.Id, terms, registryNumber, isPrimary: true);
        employment.AddAssignment(assignment, terms.HireDate, AssignmentReason.Hire);
        employee._employments.Add(employment);
        return employee;
    }

    public void UpdatePerson(PersonProfile person) => ApplyPerson(person);

    public void SetContacts(
        IEnumerable<EmployeeEmail> emails,
        IEnumerable<EmployeePhone> phones,
        IEnumerable<EmployeeAddress> addresses,
        IEnumerable<EmergencyContact> emergencyContacts)
    {
        Emails = OnePrimary(Bounded(emails, nameof(emails)), e => e.IsPrimary, nameof(emails));
        Phones = OnePrimary(Bounded(phones, nameof(phones)), p => p.IsPrimary, nameof(phones));
        Addresses = Bounded(addresses, nameof(addresses));
        EmergencyContacts = Bounded(emergencyContacts, nameof(emergencyContacts));
    }

    public void SetFamily(IEnumerable<FamilyMember> members) => FamilyMembers = Bounded(members, nameof(members));

    public void SetQualifications(IEnumerable<Education> educations, IEnumerable<Certificate> certificates, IEnumerable<LanguageSkill> languages)
    {
        Educations = Bounded(educations, nameof(educations));
        Certificates = Bounded(certificates, nameof(certificates));
        Languages = Bounded(languages, nameof(languages));
    }

    public void SetIban(Iban? iban) => Iban = iban?.Value;

    public void SetCustomFields(CustomFieldValues values) => CustomFields = values ?? throw new ArgumentNullException(nameof(values));

    /// <summary>A new employment of a person who has none open in that company (tekrar işe giriş).</summary>
    public Result<Employment> Rehire(EmploymentTerms terms, AssignmentDetails assignment, string registryNumber)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var check = EmploymentRules.Check(terms, requireSeniorityReason: true);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var available = CheckCompanyAvailable(terms.CompanyId, terms.HireDate, "companyId", "hireDate");
        if (available.IsFailure)
        {
            return available.Error;
        }

        var employment = new Employment(Id, terms, registryNumber, isPrimary: !_employments.Any(e => e.IsOpen));
        employment.AddAssignment(assignment, terms.HireDate, AssignmentReason.Hire);
        _employments.Add(employment);

        // A later leave asks for the deactivation again
        UserDeactivationRequestedAt = null;
        return employment;
    }

    /// <summary>The leave policy of an open employment set by hand, or null to follow the assignment rules (D41).</summary>
    public Result SetLeavePolicy(EmploymentId employmentId, Guid? policyId)
    {
        var found = OpenEmployment(employmentId);
        if (found.IsFailure)
        {
            return found.Error;
        }

        found.Value.SetLeavePolicy(policyId);
        return Result.Success();
    }

    /// <summary>
    /// A new assignment from <paramref name="validFrom"/>, which may be in the future; the previous one ends the day
    /// before. A change starting on the latest assignment's first day replaces it (correction).
    /// </summary>
    public Result<JobAssignment> ChangeAssignment(EmploymentId employmentId, AssignmentDetails details, DateOnly validFrom, AssignmentReason reason)
    {
        var found = OpenEmployment(employmentId);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employment = found.Value;
        if (validFrom < employment.HireDate)
        {
            return EmployeeErrors.AssignmentBeforeHire;
        }

        var latest = employment.LatestAssignment!;
        if (validFrom == latest.ValidFrom)
        {
            latest.Apply(details, latest.Reason == AssignmentReason.Hire ? AssignmentReason.Hire : reason);
            return latest;
        }

        if (validFrom < latest.ValidFrom)
        {
            return EmployeeErrors.AssignmentBeforeLatest;
        }

        latest.EndOn(validFrom.AddDays(-1));
        return employment.AddAssignment(details, validFrom, reason);
    }

    /// <summary>
    /// Ends an employment on its last working day: the assignment of that day ends with it and later ones are
    /// dropped. Another open employment becomes the primary one.
    /// </summary>
    public Result Terminate(EmploymentId employmentId, TerminationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var found = OpenEmployment(employmentId);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var employment = found.Value;
        if (details.Date < employment.HireDate)
        {
            return EmployeeErrors.TerminationBeforeHire;
        }

        foreach (var later in employment.Assignments.Where(a => a.ValidFrom > details.Date).ToList())
        {
            employment.RemoveAssignment(later);
        }

        var last = employment.LatestAssignment;
        if (last is not null && (last.ValidTo is null || last.ValidTo > details.Date))
        {
            last.EndOn(details.Date);
        }

        var wasPrimary = employment.IsPrimary;
        employment.End(details);
        if (wasPrimary)
        {
            _employments.Where(e => e.IsOpen).OrderBy(e => e.HireDate).FirstOrDefault()?.MakePrimary(true);
        }

        return Result.Success();
    }

    /// <summary>
    /// Moves the person to another company of the group (holding içi nakil): the source employment ends the day
    /// before the target's hire date (the transfer date) and a new one starts on it. Seniority is kept when asked.
    /// </summary>
    public Result<Employment> GroupTransfer(
        EmploymentId sourceId,
        EmploymentTerms target,
        string sgkExitCode,
        bool keepSeniority,
        AssignmentDetails assignment,
        string registryNumber)
    {
        ArgumentNullException.ThrowIfNull(target);
        var found = OpenEmployment(sourceId);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var source = found.Value;
        if (target.CompanyId == source.CompanyId)
        {
            return EmployeeErrors.TransferToSameCompany;
        }

        if (target.HireDate <= source.HireDate)
        {
            return EmployeeErrors.TransferBeforeHire;
        }

        var terms = keepSeniority ? target with { SeniorityDate = source.SeniorityDate, SeniorityReason = source.SeniorityReason } : target;
        var check = EmploymentRules.Check(terms, requireSeniorityReason: !keepSeniority);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var available = CheckCompanyAvailable(target.CompanyId, target.HireDate, "targetCompanyId", "transferDate");
        if (available.IsFailure)
        {
            return available.Error;
        }

        var wasPrimary = source.IsPrimary;
        var ended = Terminate(sourceId, new TerminationDetails(target.HireDate.AddDays(-1), sgkExitCode, null, true, null));
        if (ended.IsFailure)
        {
            return ended.Error;
        }

        // Terminate passed the primary flag on; the person's main job moves with them
        if (wasPrimary)
        {
            _employments.ForEach(e => e.MakePrimary(false));
        }

        var employment = new Employment(Id, terms, registryNumber, isPrimary: wasPrimary || !_employments.Any(e => e.IsOpen));
        employment.AddAssignment(assignment, target.HireDate, AssignmentReason.Transfer);
        _employments.Add(employment);
        return employment;
    }

    /// <summary>A request to create or link a user is on its way to Platform; one at a time.</summary>
    public Result RequestUserLink()
    {
        if (UserLinkState == UserLinkState.Pending)
        {
            return EmployeeErrors.UserLinkPending;
        }

        if (UserId is not null)
        {
            return EmployeeErrors.UserAlreadyLinked;
        }

        UserLinkState = UserLinkState.Pending;
        UserLinkError = null;
        return Result.Success();
    }

    /// <summary>A request to remove the link is on its way to Platform.</summary>
    public Result RequestUserUnlink()
    {
        if (UserLinkState == UserLinkState.Pending)
        {
            return EmployeeErrors.UserLinkPending;
        }

        if (UserId is null)
        {
            return EmployeeErrors.NoUser;
        }

        UserLinkState = UserLinkState.Pending;
        return Result.Success();
    }

    /// <summary>Platform linked the user (<c>UserLinkedToEmployee</c>).</summary>
    public void UserLinked(Guid userId)
    {
        UserId = userId;
        UserLinkState = UserLinkState.Linked;
        UserLinkError = null;
        UserDeactivationRequestedAt = null;
    }

    /// <summary>Platform removed the link (<c>UserUnlinkedFromEmployee</c>); a link to another user stays.</summary>
    public void UserUnlinked(Guid userId)
    {
        if (UserId is { } current && current != userId)
        {
            UserLinkState = UserLinkState.Linked;
            return;
        }

        UserId = null;
        UserLinkState = UserLinkState.None;
        UserLinkError = null;
    }

    /// <summary>Platform refused the request (<c>UserProvisioningFailed</c>).</summary>
    public void UserLinkFailed(string errorCode)
    {
        UserLinkState = UserId is null ? UserLinkState.Failed : UserLinkState.Linked;
        UserLinkError = Text.Required(errorCode, 100, nameof(errorCode));
    }

    /// <summary>Records that the user's deactivation was asked for, so it is asked once.</summary>
    public void MarkUserDeactivationRequested(DateTimeOffset at) => UserDeactivationRequestedAt = at;

    /// <summary>Records that the assignment's change was announced to other modules (<c>EmployeeAssignmentChanged</c>).</summary>
    public void MarkAnnounced(JobAssignmentId assignmentId) =>
        _employments.SelectMany(e => e.Assignments).Single(a => a.Id == assignmentId).MarkAnnounced();

    /// <summary>The employment that gives the person's primary position on <paramref name="day"/>.</summary>
    public Employment? PrimaryEmploymentOn(DateOnly day) =>
        _employments
            .Where(e => e.StatusOn(day) == EmploymentStatus.Active)
            .OrderByDescending(e => e.IsPrimary)
            .ThenBy(e => e.HireDate)
            .FirstOrDefault();

    private Result<Employment> OpenEmployment(EmploymentId employmentId)
    {
        var employment = _employments.SingleOrDefault(e => e.Id == employmentId);
        if (employment is null)
        {
            return CommonErrors.NotFound;
        }

        return employment.IsOpen ? employment : EmployeeErrors.EmploymentClosed;
    }

    private Result CheckCompanyAvailable(Guid companyId, DateOnly start, string companyField, string dateField)
    {
        var inCompany = _employments.Where(e => e.CompanyId == companyId).ToList();
        if (inCompany.Any(e => e.IsOpen))
        {
            return EmployeeErrors.OpenEmploymentExists(companyField);
        }

        return inCompany.Any(e => e.TerminationDate >= start) ? EmployeeErrors.StartsBeforePreviousEnd(dateField) : Result.Success();
    }

    private void ApplyPerson(PersonProfile person)
    {
        ArgumentNullException.ThrowIfNull(person);
        FirstName = Text.Required(person.FirstName, NameMaxLength, nameof(person.FirstName));
        LastName = Text.Required(person.LastName, NameMaxLength, nameof(person.LastName));
        BirthDate = person.BirthDate;
        BirthPlace = Text.Optional(person.BirthPlace, PlaceMaxLength, nameof(person.BirthPlace));
        Gender = person.Gender;
        Nationality = Text.Optional(person.Nationality, 2, nameof(person.Nationality))?.ToUpperInvariant();
        MaritalStatus = person.MaritalStatus;
        MilitaryStatus = person.MilitaryStatus;
    }

    private static List<T> Bounded<T>(IEnumerable<T> items, string name)
    {
        ArgumentNullException.ThrowIfNull(items, name);
        var list = items.ToList();
        return list.Count <= MaxListItems ? list : throw new ArgumentException($"At most {MaxListItems} {name}.", name);
    }

    private static List<T> OnePrimary<T>(List<T> items, Func<T, bool> isPrimary, string name) =>
        items.Count(isPrimary) <= 1 ? items : throw new ArgumentException($"At most one primary item in {name}.", name);
}

/// <summary>Checks of employment terms shared by hiring, rehiring and transfers.</summary>
internal static class EmploymentRules
{
    public const decimal MaxWeeklyHours = 45;

    public static Result Check(EmploymentTerms terms, bool requireSeniorityReason)
    {
        if (terms.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("An employment belongs to a company.", nameof(terms));
        }

        var errors = new List<FieldError>();
        if (terms.SeniorityDate > terms.HireDate)
        {
            errors.Add(new FieldError("seniorityDate", EmployeeErrors.SeniorityAfterHireCode));
        }
        else if (requireSeniorityReason && terms.SeniorityDate is { } seniority && seniority != terms.HireDate && string.IsNullOrWhiteSpace(terms.SeniorityReason))
        {
            errors.Add(new FieldError("seniorityReason", EmployeeErrors.SeniorityReasonRequiredCode));
        }

        if (terms.ProbationEndDate < terms.HireDate)
        {
            errors.Add(new FieldError("probationEndDate", EmployeeErrors.BeforeHireCode));
        }

        if (terms.Type == EmploymentType.FixedTerm && (terms.ContractEndDate is null || terms.ContractEndDate < terms.HireDate))
        {
            errors.Add(new FieldError("contractEndDate", terms.ContractEndDate is null ? CommonErrors.Required.Code : EmployeeErrors.BeforeHireCode));
        }

        if (terms.Type == EmploymentType.PartTime && terms.WeeklyHours is not (> 0 and <= MaxWeeklyHours))
        {
            errors.Add(new FieldError("weeklyHours", EmployeeErrors.WeeklyHoursCode, new Dictionary<string, object?> { ["max"] = MaxWeeklyHours }));
        }

        return errors.Count == 0 ? Result.Success() : Error.Validation(errors);
    }
}

public static class EmployeeErrors
{
    public const string SeniorityAfterHireCode = "hr.employees.errors.seniority_after_hire";
    public const string SeniorityReasonRequiredCode = "hr.employees.errors.seniority_reason_required";
    public const string BeforeHireCode = "hr.employees.errors.before_hire";
    public const string WeeklyHoursCode = "hr.employees.errors.weekly_hours";

    public static readonly Error EmploymentClosed = Error.Validation([new FieldError("employmentId", "hr.employees.errors.employment_closed")]);
    public static readonly Error AssignmentBeforeHire = Error.Validation([new FieldError("validFrom", BeforeHireCode)]);
    public static readonly Error AssignmentBeforeLatest = Error.Validation([new FieldError("validFrom", "hr.employees.errors.assignment_before_latest")]);
    public static readonly Error TerminationBeforeHire = Error.Validation([new FieldError("date", BeforeHireCode)]);
    public static readonly Error TransferToSameCompany = Error.Validation([new FieldError("targetCompanyId", "hr.employees.errors.transfer_same_company")]);
    public static readonly Error TransferBeforeHire = Error.Validation([new FieldError("transferDate", "hr.employees.errors.transfer_before_hire")]);

    public static Error OpenEmploymentExists(string field) => Error.Validation([new FieldError(field, "hr.employees.errors.open_employment_exists")]);

    public static readonly Error UserLinkPending = Error.Conflict("hr.employees.errors.user_link_pending");
    public static readonly Error UserAlreadyLinked = Error.Conflict("hr.employees.errors.user_already_linked");
    public static readonly Error NoUser = Error.Conflict("hr.employees.errors.no_user");

    public static Error StartsBeforePreviousEnd(string field) => Error.Validation([new FieldError(field, "hr.employees.errors.starts_before_previous_end")]);
}
