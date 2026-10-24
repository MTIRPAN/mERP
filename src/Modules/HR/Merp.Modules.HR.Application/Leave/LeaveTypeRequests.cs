using System.Globalization;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>
/// The leave types of every company and of <paramref name="CompanyId"/> (or of all companies the caller reads), active
/// ones unless asked (D41).
/// </summary>
public sealed record GetLeaveTypesQuery(Guid? CompanyId = null, bool IncludeInactive = false) : IQuery<IReadOnlyList<LeaveTypeItem>>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Settings.Read;
}

/// <remarks>
/// Name is in the caller's language, Names in every language. Warnings are the versions' settings below the law.
/// Send Version back when updating.
/// </remarks>
public sealed record LeaveTypeItem(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyDictionary<string, string> Names,
    Guid? CompanyId,
    LeaveCategory Category,
    LeavePayer Payer,
    string? DeductsFrom,
    LeaveUnit Unit,
    DurationBasis DurationBasis,
    bool RequiresDocument,
    bool RequiresApproval,
    Gender? GenderRestriction,
    bool AllowPastDates,
    string? LegalBasis,
    bool IsActive,
    IReadOnlyList<LeaveTypeVersion> Versions,
    IReadOnlyList<LeaveLegalWarning> Warnings,
    uint Version);

/// <summary>Fields shared by creating and changing a leave type.</summary>
public interface ILeaveTypeFields
{
    IReadOnlyDictionary<string, string?>? Name { get; }

    LeaveCategory? Category { get; }

    LeavePayer? Payer { get; }

    /// <summary>The code of the leave type whose balance it uses, e.g. YILLIK; its own code for an annual leave.</summary>
    string? DeductsFrom { get; }

    LeaveUnit? Unit { get; }

    DurationBasis? DurationBasis { get; }

    bool RequiresDocument { get; }

    bool RequiresApproval { get; }

    Gender? GenderRestriction { get; }

    bool AllowPastDates { get; }

    string? LegalBasis { get; }

    /// <summary>The limits from their first days; the first version may be <c>0001-01-01</c>, from the beginning.</summary>
    IReadOnlyList<LeaveTypeVersion>? Versions { get; }
}

/// <summary>Adds a leave type, of every company (Tenant scope) or of one company (D41).</summary>
public sealed record CreateLeaveTypeCommand(
    string? Code,
    IReadOnlyDictionary<string, string?>? Name,
    Guid? CompanyId,
    LeaveCategory? Category,
    LeavePayer? Payer,
    LeaveUnit? Unit,
    DurationBasis? DurationBasis,
    IReadOnlyList<LeaveTypeVersion>? Versions,
    string? DeductsFrom = null,
    bool RequiresDocument = false,
    bool RequiresApproval = true,
    Gender? GenderRestriction = null,
    bool AllowPastDates = true,
    string? LegalBasis = null) : ICommand<Guid>, IRequirePermission, ILeaveTypeFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Settings.Manage;
}

/// <summary>
/// Changes a leave type; the code and the company never change. Versions that have started stay as they are; versions
/// to come may be added, changed or removed (D41).
/// </summary>
public sealed record UpdateLeaveTypeCommand(
    uint Version,
    IReadOnlyDictionary<string, string?>? Name,
    LeaveCategory? Category,
    LeavePayer? Payer,
    LeaveUnit? Unit,
    DurationBasis? DurationBasis,
    IReadOnlyList<LeaveTypeVersion>? Versions,
    bool IsActive,
    string? DeductsFrom = null,
    bool RequiresDocument = false,
    bool RequiresApproval = true,
    Gender? GenderRestriction = null,
    bool AllowPastDates = true,
    string? LegalBasis = null) : ICommand, IRequirePermission, ILeaveTypeFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Settings.Manage;
}

internal static class LeaveTypeRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator, ILocalizationSource localization)
        where T : ILeaveTypeFields
    {
        LeaveSettings.AddNameRules(validator, t => t.Name, localization, LeaveType.NameMaxLength);
        validator.RuleFor(t => t.Category).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        validator.RuleFor(t => t.Payer).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        validator.RuleFor(t => t.Unit).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        validator.RuleFor(t => t.DurationBasis).Cascade(CascadeMode.Stop).NotNull().IsInEnum();
        validator.RuleFor(t => t.GenderRestriction).IsInEnum();
        validator.RuleFor(t => t.LegalBasis).MaximumLength(LeaveType.LegalBasisMaxLength);
        validator.RuleFor(t => t.DeductsFrom).MustBeValid(v => Code.Create(v, LeaveType.CodeMaxLength)).When(t => !string.IsNullOrWhiteSpace(t.DeductsFrom));
        validator.RuleFor(t => t.Versions).NotEmpty().Must(v => v!.Count <= LeaveSettings.MaxVersions).WithErrorCode(CommonErrors.ValidationCode);
        validator.RuleForEach(t => t.Versions).ChildRules(version =>
        {
            version.RuleFor(v => v.FixedDays).InclusiveBetween(0, LeaveSettings.MaxDays);
            version.RuleFor(v => v.DaysBeforeEvent).InclusiveBetween(0, LeaveSettings.MaxDays);
            version.RuleFor(v => v.DaysBeforeEvent).LessThanOrEqualTo(v => v.FixedDays).WithErrorCode(LeaveSettingsErrors.BeforeEventAboveFixedCode)
                .When(v => v.DaysBeforeEvent is not null && v.FixedDays is not null);
            version.RuleFor(v => v.MultipleBirthExtraDays).InclusiveBetween(0, LeaveSettings.MaxDays);
            version.RuleFor(v => v.MaxDaysPerYear).InclusiveBetween(0, 366);
            version.RuleFor(v => v.MaxDaysPerRequest).InclusiveBetween(0, LeaveSettings.MaxDays);
            version.RuleFor(v => v.DailyHours).InclusiveBetween(0, 24);
            version.RuleFor(v => v.MinNoticeDays).InclusiveBetween(0, 365);
        });
    }

    public static LeaveTypeProfile ToProfile(ILeaveTypeFields fields) =>
        new(
            fields.Category!.Value,
            fields.Payer!.Value,
            string.IsNullOrWhiteSpace(fields.DeductsFrom) ? null : fields.DeductsFrom,
            fields.Unit!.Value,
            fields.DurationBasis!.Value,
            fields.RequiresDocument,
            fields.RequiresApproval,
            fields.GenderRestriction,
            fields.AllowPastDates,
            fields.LegalBasis);

    /// <summary>The balance it uses is a leave type of the tenant, or the type itself.</summary>
    public static async Task<FieldError?> CheckDeductsFromAsync(IHrDbContext db, string? deductsFrom, string ownCode, CancellationToken cancellationToken)
    {
        var code = string.IsNullOrWhiteSpace(deductsFrom) ? null : deductsFrom.Trim().ToUpperInvariant();
        return code is null || code == ownCode || await db.LeaveTypes.AnyAsync(t => t.Code == code, cancellationToken)
            ? null
            : new FieldError("deductsFrom", LeaveSettingsErrors.UnknownLeaveTypeCode);
    }

    /// <summary>The day versions start counting as history, in the type's company or Turkey.</summary>
    public static async Task<DateOnly> TodayAsync(IOrganizationLookup organization, TimeProvider timeProvider, Guid? companyId, CancellationToken cancellationToken)
    {
        var zone = companyId is { } id ? (await organization.GetCompaniesAsync([id], cancellationToken)).SingleOrDefault()?.TimeZoneId : null;
        return HrClock.Today(timeProvider, zone);
    }
}

internal sealed class CreateLeaveTypeCommandValidator : AbstractValidator<CreateLeaveTypeCommand>
{
    public CreateLeaveTypeCommandValidator(ILocalizationSource localization)
    {
        RuleFor(t => t.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, LeaveType.CodeMaxLength));
        LeaveTypeRules.AddFieldRules(this, localization);
    }
}

internal sealed class UpdateLeaveTypeCommandValidator : AbstractValidator<UpdateLeaveTypeCommand>
{
    public UpdateLeaveTypeCommandValidator(ILocalizationSource localization) => LeaveTypeRules.AddFieldRules(this, localization);
}

internal sealed class GetLeaveTypesQueryHandler(IHrDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, ITextLocalizer localizer)
    : IQueryHandler<GetLeaveTypesQuery, IReadOnlyList<LeaveTypeItem>>
{
    public async Task<Result<IReadOnlyList<LeaveTypeItem>>> Handle(GetLeaveTypesQuery request, CancellationToken cancellationToken)
    {
        var readable = await LeaveSettings.ReadableAsync(scopes, cancellationToken);
        if (request.CompanyId is { } companyId && !readable(companyId))
        {
            return CommonErrors.Forbidden;
        }

        var types = await db.LeaveTypes.AsNoTracking()
            .Where(t => request.IncludeInactive || t.IsActive)
            .Where(t => request.CompanyId == null || t.CompanyId == null || t.CompanyId == request.CompanyId)
            .Select(t => new { Type = t, Version = EF.Property<uint>(t, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo(currentUser.Culture), ignoreCase: true);
        return types
            .Where(t => readable(t.Type.CompanyId))
            .Select(t => new LeaveTypeItem(
                t.Type.Id.Value,
                t.Type.Code,
                t.Type.Name.Get(currentUser.Culture, "tr-TR"),
                t.Type.Name.Values,
                t.Type.CompanyId,
                t.Type.Category,
                t.Type.Payer,
                t.Type.DeductsFrom,
                t.Type.Unit,
                t.Type.DurationBasis,
                t.Type.RequiresDocument,
                t.Type.RequiresApproval,
                t.Type.GenderRestriction,
                t.Type.AllowPastDates,
                t.Type.LegalBasis,
                t.Type.IsActive,
                t.Type.Versions,
                LeaveSettings.Localize(LegalMinimums.Check(t.Type.Code, t.Type.Versions), localizer),
                t.Version))
            .OrderBy(t => t.Name, comparer)
            .ToList();
    }
}

internal sealed class CreateLeaveTypeCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization)
    : ICommandHandler<CreateLeaveTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateLeaveTypeCommand request, CancellationToken cancellationToken)
    {
        if (await LeaveSettings.CheckCompanyAsync(scopes, organization, request.CompanyId, cancellationToken) is { } companyError)
        {
            return companyError;
        }

        var code = Code.Create(request.Code, LeaveType.CodeMaxLength).Value;
        var errors = new List<FieldError>();
        if (await db.LeaveTypes.AnyAsync(t => t.Code == code.Value, cancellationToken))
        {
            errors.Add(new FieldError("code", CommonErrors.Duplicate.Code));
        }

        if (await LeaveTypeRules.CheckDeductsFromAsync(db, request.DeductsFrom, code.Value, cancellationToken) is { } deductsError)
        {
            errors.Add(deductsError);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var created = LeaveType.Create(code, LocalizedString.Create(request.Name).Value, request.CompanyId, LeaveTypeRules.ToProfile(request), request.Versions!);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.LeaveTypes.Add(created.Value);
        return created.Value.Id.Value;
    }
}

internal sealed class UpdateLeaveTypeCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization, TimeProvider timeProvider)
    : ICommandHandler<UpdateLeaveTypeCommand>
{
    public async Task<Result> Handle(UpdateLeaveTypeCommand request, CancellationToken cancellationToken)
    {
        var type = await db.LeaveTypes.SingleOrDefaultAsync(t => t.Id == LeaveTypeId.From(request.Id), cancellationToken);
        if (type is null)
        {
            return CommonErrors.NotFound;
        }

        if (await LeaveSettings.CheckCompanyAsync(scopes, organization, type.CompanyId, cancellationToken, requireActive: false) is { } companyError)
        {
            return companyError;
        }

        if (await LeaveTypeRules.CheckDeductsFromAsync(db, request.DeductsFrom, type.Code, cancellationToken) is { } deductsError)
        {
            return Error.Validation([deductsError]);
        }

        db.Entry(type).ExpectVersion(request.Version);
        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, type.CompanyId, cancellationToken);
        return type.Update(LocalizedString.Create(request.Name).Value, LeaveTypeRules.ToProfile(request), request.IsActive, request.Versions!, today);
    }
}
