using System.Globalization;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Application.Persistence;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>The leave policies of every company and of the companies the caller reads (D41).</summary>
public sealed record GetLeavePoliciesQuery(Guid? CompanyId = null, bool IncludeInactive = false) : IQuery<IReadOnlyList<LeavePolicyItem>>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Settings.Read;
}

/// <remarks>Warnings are the versions' settings below the law. Send Version back when updating.</remarks>
public sealed record LeavePolicyItem(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyDictionary<string, string> Names,
    Guid? CompanyId,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<LeavePolicyVersion> Versions,
    IReadOnlyList<LeaveLegalWarning> Warnings,
    uint Version);

/// <summary>The settings of annual leave rules below the law, for the policy form while it is edited (D41).</summary>
public sealed record CheckLeavePolicyRulesQuery(AnnualLeaveRules? Rules) : IQuery<IReadOnlyList<LeaveLegalWarning>>, IRequirePermission
{
    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Settings.Read;
}

/// <summary>
/// Adds a leave policy, of every company (Tenant scope) or of one company. As the default it applies to the employees
/// of its company (or of every company) whom no rule and no override gives a policy (D41).
/// </summary>
public sealed record CreateLeavePolicyCommand(
    string? Code,
    IReadOnlyDictionary<string, string?>? Name,
    Guid? CompanyId,
    IReadOnlyList<LeavePolicyVersion>? Versions,
    bool IsDefault = false) : ICommand<Guid>, IRequirePermission, ILeavePolicyFields
{
    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Settings.Manage;
}

/// <summary>
/// Changes a leave policy; the code and the company never change. Versions that have started stay as they are (D41).
/// </summary>
public sealed record UpdateLeavePolicyCommand(
    uint Version,
    IReadOnlyDictionary<string, string?>? Name,
    IReadOnlyList<LeavePolicyVersion>? Versions,
    bool IsDefault,
    bool IsActive) : ICommand, IRequirePermission, ILeavePolicyFields
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Settings.Manage;
}

public interface ILeavePolicyFields
{
    IReadOnlyDictionary<string, string?>? Name { get; }

    /// <summary>The rules from their first days; the first version may be <c>0001-01-01</c>, from the beginning.</summary>
    IReadOnlyList<LeavePolicyVersion>? Versions { get; }
}

/// <summary>The ranges of annual leave rules; settings below the law are allowed and warned about.</summary>
internal sealed class AnnualLeaveRulesValidator : AbstractValidator<AnnualLeaveRules>
{
    public AnnualLeaveRulesValidator()
    {
        RuleFor(r => r.MinServiceYearsForEntitlement).InclusiveBetween(0, 50);
        RuleFor(r => r.Tiers).NotEmpty().Must(t => t.Count <= AnnualLeaveRules.MaxRules).WithErrorCode(CommonErrors.ValidationCode)
            .Must(t => t.Select(x => x.MinServiceYears).Distinct().Count() == t.Count).WithErrorCode(CommonErrors.Duplicate.Code);
        RuleForEach(r => r.Tiers).ChildRules(tier =>
        {
            tier.RuleFor(t => t.MinServiceYears).InclusiveBetween(0, 60);
            tier.RuleFor(t => t.Days).InclusiveBetween(0, 366);
        });
        RuleFor(r => r.AgeMinimums).NotNull().Must(a => a.Count <= AnnualLeaveRules.MaxRules).WithErrorCode(CommonErrors.ValidationCode);
        RuleForEach(r => r.AgeMinimums).ChildRules(age =>
        {
            age.RuleFor(a => a.MaxAge).InclusiveBetween(0, 100);
            age.RuleFor(a => a.MinAge).InclusiveBetween(0, 100);
            age.RuleFor(a => a.MinDays).InclusiveBetween(0, 366);
            age.RuleFor(a => a.MaxAge).NotNull().When(a => a.MinAge is null);
        });
        RuleFor(r => r.ExtraDayRules).NotNull().Must(e => e.Count <= AnnualLeaveRules.MaxRules).WithErrorCode(CommonErrors.ValidationCode);
        RuleForEach(r => r.ExtraDayRules).ChildRules(extra =>
        {
            extra.RuleFor(e => e.Criterion).NotNull().SetValidator(new PolicyCriterionValidator());
            extra.RuleFor(e => e.Days).InclusiveBetween(0, 366);
        });
        RuleFor(r => r.AccrualMethod).IsInEnum();
        RuleFor(r => r.TenDayBlockRule).IsInEnum();
        RuleFor(r => r.Rounding).IsInEnum();
        RuleFor(r => r.MaxTravelDays).InclusiveBetween(0, 30);
        RuleFor(r => r.CarryOverLimitDays).InclusiveBetween(0, LeaveSettings.MaxDays);
        RuleFor(r => r.AdvanceLimitDays).InclusiveBetween(0, 366);
        RuleFor(r => r.MinNoticeDays).InclusiveBetween(0, 365);
        RuleFor(r => r.MaxDaysPerRequest).InclusiveBetween(0.5m, LeaveSettings.MaxDays);
    }
}

internal static class LeavePolicyFieldRules
{
    public static void AddFieldRules<T>(AbstractValidator<T> validator, ILocalizationSource localization)
        where T : ILeavePolicyFields
    {
        LeaveSettings.AddNameRules(validator, p => p.Name, localization, LeavePolicy.NameMaxLength);
        validator.RuleFor(p => p.Versions).NotEmpty().Must(v => v!.Count <= LeaveSettings.MaxVersions).WithErrorCode(CommonErrors.ValidationCode);
        validator.RuleForEach(p => p.Versions).ChildRules(version => version.RuleFor(v => v.Rules).NotNull().SetValidator(new AnnualLeaveRulesValidator()));
    }

    public static IReadOnlyList<LeaveLegalWarning> Warnings(IEnumerable<LeavePolicyVersion> versions, ITextLocalizer localizer) =>
        LeaveSettings.Localize(versions.SelectMany(v => LegalMinimums.Check(v.Rules, v.ValidFrom)), localizer);
}

internal sealed class CheckLeavePolicyRulesQueryValidator : AbstractValidator<CheckLeavePolicyRulesQuery>
{
    public CheckLeavePolicyRulesQueryValidator() => RuleFor(q => q.Rules).NotNull().SetValidator(new AnnualLeaveRulesValidator()!);
}

internal sealed class CreateLeavePolicyCommandValidator : AbstractValidator<CreateLeavePolicyCommand>
{
    public CreateLeavePolicyCommandValidator(ILocalizationSource localization)
    {
        RuleFor(p => p.Code).Cascade(CascadeMode.Stop).NotEmpty().MustBeValid(v => Code.Create(v, LeavePolicy.CodeMaxLength));
        LeavePolicyFieldRules.AddFieldRules(this, localization);
    }
}

internal sealed class UpdateLeavePolicyCommandValidator : AbstractValidator<UpdateLeavePolicyCommand>
{
    public UpdateLeavePolicyCommandValidator(ILocalizationSource localization) => LeavePolicyFieldRules.AddFieldRules(this, localization);
}

internal sealed class GetLeavePoliciesQueryHandler(IHrDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, ITextLocalizer localizer)
    : IQueryHandler<GetLeavePoliciesQuery, IReadOnlyList<LeavePolicyItem>>
{
    public async Task<Result<IReadOnlyList<LeavePolicyItem>>> Handle(GetLeavePoliciesQuery request, CancellationToken cancellationToken)
    {
        var readable = await LeaveSettings.ReadableAsync(scopes, cancellationToken);
        if (request.CompanyId is { } companyId && !readable(companyId))
        {
            return CommonErrors.Forbidden;
        }

        var policies = await db.LeavePolicies.AsNoTracking()
            .Where(p => request.IncludeInactive || p.IsActive)
            .Where(p => request.CompanyId == null || p.CompanyId == null || p.CompanyId == request.CompanyId)
            .Select(p => new { Policy = p, Version = EF.Property<uint>(p, ConcurrencyVersion.PropertyName) })
            .ToListAsync(cancellationToken);
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo(currentUser.Culture), ignoreCase: true);
        return policies
            .Where(p => readable(p.Policy.CompanyId))
            .Select(p => new LeavePolicyItem(
                p.Policy.Id.Value,
                p.Policy.Code,
                p.Policy.Name.Get(currentUser.Culture, "tr-TR"),
                p.Policy.Name.Values,
                p.Policy.CompanyId,
                p.Policy.IsDefault,
                p.Policy.IsActive,
                p.Policy.Versions,
                LeavePolicyFieldRules.Warnings(p.Policy.Versions, localizer),
                p.Version))
            .OrderBy(p => p.Name, comparer)
            .ToList();
    }
}

internal sealed class CheckLeavePolicyRulesQueryHandler(ITextLocalizer localizer) : IQueryHandler<CheckLeavePolicyRulesQuery, IReadOnlyList<LeaveLegalWarning>>
{
    public Task<Result<IReadOnlyList<LeaveLegalWarning>>> Handle(CheckLeavePolicyRulesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<LeaveLegalWarning>>>(LeaveSettings.Localize(LegalMinimums.Check(request.Rules!), localizer).ToList());
}

internal sealed class CreateLeavePolicyCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization)
    : ICommandHandler<CreateLeavePolicyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateLeavePolicyCommand request, CancellationToken cancellationToken)
    {
        if (await LeaveSettings.CheckCompanyAsync(scopes, organization, request.CompanyId, cancellationToken) is { } companyError)
        {
            return companyError;
        }

        var code = Code.Create(request.Code, LeavePolicy.CodeMaxLength).Value;
        if (await db.LeavePolicies.AnyAsync(p => p.Code == code.Value, cancellationToken))
        {
            return Error.Validation([new FieldError("code", CommonErrors.Duplicate.Code)]);
        }

        var created = LeavePolicy.Create(code, LocalizedString.Create(request.Name).Value, request.CompanyId, request.IsDefault, request.Versions!);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.LeavePolicies.Add(created.Value);
        return created.Value.Id.Value;
    }
}

internal sealed class UpdateLeavePolicyCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization, TimeProvider timeProvider)
    : ICommandHandler<UpdateLeavePolicyCommand>
{
    public async Task<Result> Handle(UpdateLeavePolicyCommand request, CancellationToken cancellationToken)
    {
        var policy = await db.LeavePolicies.SingleOrDefaultAsync(p => p.Id == LeavePolicyId.From(request.Id), cancellationToken);
        if (policy is null)
        {
            return CommonErrors.NotFound;
        }

        if (await LeaveSettings.CheckCompanyAsync(scopes, organization, policy.CompanyId, cancellationToken, requireActive: false) is { } companyError)
        {
            return companyError;
        }

        db.Entry(policy).ExpectVersion(request.Version);
        var today = await LeaveTypeRules.TodayAsync(organization, timeProvider, policy.CompanyId, cancellationToken);
        return policy.Update(LocalizedString.Create(request.Name).Value, request.IsDefault, request.IsActive, request.Versions!, today);
    }
}

/// <summary>One default per company and one for every company: the previous default stops being it (D41).</summary>
internal sealed class ClearOtherDefaultsOnLeavePolicyMadeDefault(IHrDbContext db) : IDomainEventHandler<LeavePolicyMadeDefault>
{
    public async Task Handle(LeavePolicyMadeDefault domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var others = await db.LeavePolicies
            .Where(p => p.IsDefault && p.CompanyId == domainEvent.CompanyId && p.Id != domainEvent.PolicyId)
            .ToListAsync(cancellationToken);
        others.ForEach(p => p.ClearDefault());
    }
}
