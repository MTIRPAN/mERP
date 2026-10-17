using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.CustomFields;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Employees;

/// <summary>Identity fields of a person, shared by hiring and the card.</summary>
public interface IPersonFields
{
    string? FirstName { get; }

    string? LastName { get; }

    DateOnly? BirthDate { get; }

    string? BirthPlace { get; }

    Gender? Gender { get; }

    /// <summary>ISO 3166-1 alpha-2, e.g. TR.</summary>
    string? Nationality { get; }

    MaritalStatus? MaritalStatus { get; }

    MilitaryStatus? MilitaryStatus { get; }
}

/// <summary>Updates the identity details and custom fields of a person; the TCKN never changes.</summary>
/// <remarks>Version is the one the client read; a concurrent change of the card ends in 409.</remarks>
public sealed record UpdateEmployeePersonCommand(
    uint Version,
    string? FirstName,
    string? LastName,
    DateOnly? BirthDate = null,
    string? BirthPlace = null,
    Gender? Gender = null,
    string? Nationality = null,
    MaritalStatus? MaritalStatus = null,
    MilitaryStatus? MilitaryStatus = null,
    IReadOnlyDictionary<string, JsonElement>? CustomFields = null) : ICommand, IRequirePermission, IPersonFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Update;
}

/// <param name="Kind">Work or Personal.</param>
/// <param name="Address">A valid e-mail address.</param>
/// <param name="IsPrimary">At most one primary address.</param>
public sealed record EmailInput(ContactKind? Kind, string? Address, bool IsPrimary);

/// <param name="Kind">Mobile, Home or Work.</param>
/// <param name="Number">A valid phone number.</param>
/// <param name="IsPrimary">At most one primary number.</param>
public sealed record PhoneInput(ContactKind? Kind, string? Number, bool IsPrimary);

/// <param name="Kind">Home or Other.</param>
/// <param name="Line">Street and number.</param>
/// <param name="District">İlçe.</param>
/// <param name="City">İl.</param>
/// <param name="PostalCode">Posta kodu.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2; TR when not given.</param>
public sealed record AddressInput(ContactKind? Kind, string? Line, string? District, string? City, string? PostalCode, string? CountryCode);

public sealed record EmergencyContactInput(string? FullName, FamilyRelation? Relation, string? Phone);

/// <summary>Replaces the e-mail addresses, phone numbers, addresses and emergency contacts of a person.</summary>
public sealed record UpdateEmployeeContactsCommand(
    uint Version,
    IReadOnlyList<EmailInput>? Emails,
    IReadOnlyList<PhoneInput>? Phones,
    IReadOnlyList<AddressInput>? Addresses,
    IReadOnlyList<EmergencyContactInput>? EmergencyContacts) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Update;
}

public sealed record FamilyMemberInput(FamilyRelation? Relation, string? FullName, DateOnly? BirthDate, bool IsWorking);

/// <summary>Replaces the family members of a person (aile bilgileri).</summary>
public sealed record UpdateEmployeeFamilyCommand(uint Version, IReadOnlyList<FamilyMemberInput>? Members) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Update;
}

public sealed record EducationInput(EducationLevel? Level, string? School, string? Department, int? GraduationYear);

public sealed record CertificateInput(CertificateKind? Kind, string? Name, string? Number, DateOnly? IssuedOn, DateOnly? ValidUntil);

/// <param name="Code">ISO 639-1 code, e.g. en.</param>
/// <param name="Level">A1 to C2, or Native.</param>
public sealed record LanguageInput(string? Code, LanguageLevel? Level);

/// <summary>Replaces the education, certificates and driving licences, and languages of a person.</summary>
public sealed record UpdateEmployeeQualificationsCommand(
    uint Version,
    IReadOnlyList<EducationInput>? Educations,
    IReadOnlyList<CertificateInput>? Certificates,
    IReadOnlyList<LanguageInput>? Languages) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Update;
}

/// <summary>Sets or clears the IBAN of a person; it is sensitive data (D38).</summary>
public sealed record SetEmployeeIbanCommand(uint Version, string? Iban) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Sensitive.Update;
}

/// <summary>
/// Replaces the special category data of a person (D13): disability degree in percent, blood type, health notes and
/// the date of the criminal record certificate. The values are stored encrypted.
/// </summary>
public sealed record UpdateEmployeeSensitiveDataCommand(
    int? DisabilityDegree,
    string? BloodType,
    string? HealthNotes,
    DateOnly? CriminalRecordDate) : ICommand, IRequirePermission
{
    public const int HealthNotesMaxLength = 2000;

    /// <summary>The blood types that can be entered.</summary>
    public static readonly IReadOnlyList<string> BloodTypes = ["0 Rh+", "0 Rh-", "A Rh+", "A Rh-", "B Rh+", "B Rh-", "AB Rh+", "AB Rh-"];

    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Employees.Sensitive.Update;
}

internal static class PersonRules
{
    public static void AddPersonRules<T>(AbstractValidator<T> validator)
        where T : IPersonFields
    {
        validator.RuleFor(c => c.FirstName).NotEmpty().MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(c => c.LastName).NotEmpty().MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(c => c.BirthPlace).MaximumLength(Employee.PlaceMaxLength);
        validator.RuleFor(c => c.Gender).IsInEnum();
        validator.RuleFor(c => c.MaritalStatus).IsInEnum();
        validator.RuleFor(c => c.MilitaryStatus).IsInEnum();
        validator.RuleFor(c => c.Nationality)
            .Must(code => code!.Trim().Length == 2 && code.Trim().All(char.IsAsciiLetter))
            .WithErrorCode(CommonErrors.InvalidCountryCode.Code)
            .When(c => !string.IsNullOrWhiteSpace(c.Nationality));
    }

    public static PersonProfile ToProfile(IPersonFields fields) =>
        new(
            fields.FirstName!,
            fields.LastName!,
            fields.BirthDate,
            fields.BirthPlace,
            fields.Gender,
            string.IsNullOrWhiteSpace(fields.Nationality) ? null : fields.Nationality.Trim(),
            fields.MaritalStatus,
            fields.MilitaryStatus);

    public static IRuleBuilderOptions<T, IReadOnlyList<TItem>?> AtMost<T, TItem>(this IRuleBuilder<T, IReadOnlyList<TItem>?> rule) =>
        rule.Must(items => items is null || items.Count <= Employee.MaxListItems)
            .WithErrorCode("hr.employees.errors.too_many_items");
}

internal sealed class UpdateEmployeePersonCommandValidator : AbstractValidator<UpdateEmployeePersonCommand>
{
    public UpdateEmployeePersonCommandValidator() => PersonRules.AddPersonRules(this);
}

internal sealed class UpdateEmployeeContactsCommandValidator : AbstractValidator<UpdateEmployeeContactsCommand>
{
    public UpdateEmployeeContactsCommandValidator()
    {
        RuleFor(c => c.Emails).AtMost();
        RuleFor(c => c.Emails).Must(items => items is null || items.Count(i => i.IsPrimary) <= 1).WithErrorCode(EmployeeCommandErrors.PrimaryCountCode);
        RuleForEach(c => c.Emails).ChildRules(email =>
        {
            email.RuleFor(e => e.Kind).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
            email.RuleFor(e => e.Address).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(Email.Create);
        });
        RuleFor(c => c.Phones).AtMost();
        RuleFor(c => c.Phones).Must(items => items is null || items.Count(i => i.IsPrimary) <= 1).WithErrorCode(EmployeeCommandErrors.PrimaryCountCode);
        RuleForEach(c => c.Phones).ChildRules(phone =>
        {
            phone.RuleFor(p => p.Kind).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
            phone.RuleFor(p => p.Number).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(PhoneNumber.Create);
        });
        RuleFor(c => c.Addresses).AtMost();
        RuleForEach(c => c.Addresses).ChildRules(address =>
        {
            address.RuleFor(a => a.Kind).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
            address.RuleFor(a => a.Line).NotEmpty().MaximumLength(Employee.TextMaxLength);
            address.RuleFor(a => a.District).MaximumLength(Employee.PlaceMaxLength);
            address.RuleFor(a => a.City).MaximumLength(Employee.PlaceMaxLength);
            address.RuleFor(a => a.PostalCode).MaximumLength(10);
            address.RuleFor(a => a.CountryCode)
                .Must(code => code!.Trim().Length == 2 && code.Trim().All(char.IsAsciiLetter))
                .WithErrorCode(CommonErrors.InvalidCountryCode.Code)
                .When(a => !string.IsNullOrWhiteSpace(a.CountryCode));
        });
        RuleFor(c => c.EmergencyContacts).AtMost();
        RuleForEach(c => c.EmergencyContacts).ChildRules(contact =>
        {
            contact.RuleFor(e => e.FullName).NotEmpty().MaximumLength(Employee.TextMaxLength);
            contact.RuleFor(e => e.Relation).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
            contact.RuleFor(e => e.Phone).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(PhoneNumber.Create);
        });
    }
}

internal sealed class UpdateEmployeeFamilyCommandValidator : AbstractValidator<UpdateEmployeeFamilyCommand>
{
    public UpdateEmployeeFamilyCommandValidator()
    {
        RuleFor(c => c.Members).AtMost();
        RuleForEach(c => c.Members).ChildRules(member =>
        {
            member.RuleFor(m => m.Relation).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
            member.RuleFor(m => m.FullName).NotEmpty().MaximumLength(Employee.TextMaxLength);
        });
    }
}

internal sealed class UpdateEmployeeQualificationsCommandValidator : AbstractValidator<UpdateEmployeeQualificationsCommand>
{
    public UpdateEmployeeQualificationsCommandValidator()
    {
        RuleFor(c => c.Educations).AtMost();
        RuleForEach(c => c.Educations).ChildRules(education =>
        {
            education.RuleFor(e => e.Level).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
            education.RuleFor(e => e.School).NotEmpty().MaximumLength(Employee.TextMaxLength);
            education.RuleFor(e => e.Department).MaximumLength(Employee.TextMaxLength);
            education.RuleFor(e => e.GraduationYear).InclusiveBetween(1900, 2200);
        });
        RuleFor(c => c.Certificates).AtMost();
        RuleForEach(c => c.Certificates).ChildRules(certificate =>
        {
            certificate.RuleFor(e => e.Kind).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
            certificate.RuleFor(e => e.Name).NotEmpty().MaximumLength(Employee.TextMaxLength);
            certificate.RuleFor(e => e.Number).MaximumLength(50);
            certificate.RuleFor(e => e.ValidUntil)
                .GreaterThanOrEqualTo(e => e.IssuedOn)
                .WithErrorCode(CommonErrors.InvalidDateRange.Code)
                .When(e => e.IssuedOn is not null && e.ValidUntil is not null);
        });
        RuleFor(c => c.Languages).AtMost();
        RuleForEach(c => c.Languages).ChildRules(language =>
        {
            language.RuleFor(l => l.Code)
                .Must(code => code is { Length: 2 } && code.All(char.IsAsciiLetterLower))
                .WithErrorCode(CommonErrors.InvalidCulture.Code);
            language.RuleFor(l => l.Level).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        });
    }
}

internal sealed class SetEmployeeIbanCommandValidator : AbstractValidator<SetEmployeeIbanCommand>
{
    public SetEmployeeIbanCommandValidator() =>
        RuleFor(c => c.Iban).MustBeValid(Iban.Create).When(c => !string.IsNullOrWhiteSpace(c.Iban));
}

internal sealed class UpdateEmployeeSensitiveDataCommandValidator : AbstractValidator<UpdateEmployeeSensitiveDataCommand>
{
    public UpdateEmployeeSensitiveDataCommandValidator()
    {
        RuleFor(c => c.DisabilityDegree).InclusiveBetween(0, 100);
        RuleFor(c => c.BloodType)
            .Must(value => UpdateEmployeeSensitiveDataCommand.BloodTypes.Contains(value!, StringComparer.Ordinal))
            .WithErrorCode("common.errors.invalid_value")
            .When(c => !string.IsNullOrWhiteSpace(c.BloodType));
        RuleFor(c => c.HealthNotes).MaximumLength(UpdateEmployeeSensitiveDataCommand.HealthNotesMaxLength);
    }
}

internal sealed class UpdateEmployeePersonCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, ICustomFieldValidator customFields)
    : ICommandHandler<UpdateEmployeePersonCommand>
{
    public async Task<Result> Handle(UpdateEmployeePersonCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.FindAsync(db, scope, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        var companyId = (employee.PrimaryEmploymentOn(DateOnly.MaxValue) ?? employee.Employments.MaxBy(e => e.HireDate))?.CompanyId;
        var values = await customFields.ValidateAsync(HrCustomFieldEntities.Employee, companyId, request.CustomFields, employee.CustomFields, cancellationToken);
        if (values.IsFailure)
        {
            return values.Error;
        }

        db.Entry(employee).ExpectVersion(request.Version);
        employee.UpdatePerson(PersonRules.ToProfile(request));
        employee.SetCustomFields(values.Value);
        return Result.Success();
    }
}

internal sealed class UpdateEmployeeContactsCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<UpdateEmployeeContactsCommand>
{
    public async Task<Result> Handle(UpdateEmployeeContactsCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.FindAsync(db, scope, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(employee).ExpectVersion(request.Version);
        employee.SetContacts(
            (request.Emails ?? []).Select(e => new EmployeeEmail(e.Kind!.Value, Email.Create(e.Address).Value.Value, e.IsPrimary)),
            (request.Phones ?? []).Select(p => new EmployeePhone(p.Kind!.Value, PhoneNumber.Create(p.Number).Value.Value, p.IsPrimary)),
            (request.Addresses ?? []).Select(a => new EmployeeAddress(
                a.Kind!.Value,
                a.Line!.Trim(),
                Blank(a.District),
                Blank(a.City),
                Blank(a.PostalCode),
                string.IsNullOrWhiteSpace(a.CountryCode) ? "TR" : a.CountryCode.Trim().ToUpperInvariant())),
            (request.EmergencyContacts ?? []).Select(c => new EmergencyContact(c.FullName!.Trim(), c.Relation!.Value, PhoneNumber.Create(c.Phone).Value.Value)));
        return Result.Success();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class UpdateEmployeeFamilyCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<UpdateEmployeeFamilyCommand>
{
    public async Task<Result> Handle(UpdateEmployeeFamilyCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.FindAsync(db, scope, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(employee).ExpectVersion(request.Version);
        employee.SetFamily((request.Members ?? []).Select(m => new FamilyMember(m.Relation!.Value, m.FullName!.Trim(), m.BirthDate, m.IsWorking)));
        return Result.Success();
    }
}

internal sealed class UpdateEmployeeQualificationsCommandHandler(IHrDbContext db, IAccessScopeProvider scopes)
    : ICommandHandler<UpdateEmployeeQualificationsCommand>
{
    public async Task<Result> Handle(UpdateEmployeeQualificationsCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.FindAsync(db, scope, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(employee).ExpectVersion(request.Version);
        employee.SetQualifications(
            (request.Educations ?? []).Select(e => new Education(e.Level!.Value, e.School!.Trim(), Blank(e.Department), e.GraduationYear)),
            (request.Certificates ?? []).Select(c => new Certificate(c.Kind!.Value, c.Name!.Trim(), Blank(c.Number), c.IssuedOn, c.ValidUntil)),
            (request.Languages ?? []).Select(l => new LanguageSkill(l.Code!, l.Level!.Value)));
        return Result.Success();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class SetEmployeeIbanCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<SetEmployeeIbanCommand>
{
    public async Task<Result> Handle(SetEmployeeIbanCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.FindAsync(db, scope, request.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return CommonErrors.NotFound;
        }

        db.Entry(employee).ExpectVersion(request.Version);
        employee.SetIban(string.IsNullOrWhiteSpace(request.Iban) ? null : Iban.Create(request.Iban).Value);
        return Result.Success();
    }
}

internal sealed class UpdateEmployeeSensitiveDataCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, ISensitiveDataProtector protector)
    : ICommandHandler<UpdateEmployeeSensitiveDataCommand>
{
    public async Task<Result> Handle(UpdateEmployeeSensitiveDataCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employeeId = EmployeeId.From(request.EmployeeId);
        var companies = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments.Select(m => m.CompanyId))
            .ToListAsync(cancellationToken);
        if (!companies.Any(scope.CoversCompany))
        {
            return CommonErrors.NotFound;
        }

        var data = await db.EmployeeSensitiveData.SingleOrDefaultAsync(s => s.Id == employeeId, cancellationToken);
        if (data is null)
        {
            data = EmployeeSensitiveData.For(employeeId);
            db.EmployeeSensitiveData.Add(data);
        }

        data.Update(new EncryptedSensitiveValues(
            protector.Protect(request.DisabilityDegree?.ToString(CultureInfo.InvariantCulture)),
            protector.Protect(request.BloodType),
            protector.Protect(request.HealthNotes),
            protector.Protect(request.CriminalRecordDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        return Result.Success();
    }
}
