using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Merp.Application.Abstractions.Context;
using Merp.Application.Abstractions.Messaging;
using Merp.Application.Abstractions.Security;
using Merp.Application.Persistence;
using Merp.Modules.HR.Application.Employees;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Employees;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.HR.Domain.Organization;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>A policy assignment rule as it is sent: all criteria must match; none matches everyone.</summary>
public sealed record LeavePolicyRuleInput(string? Name, Guid PolicyId, IReadOnlyList<PolicyCriterion>? Criteria);

/// <remarks>PolicyName is in the caller's language.</remarks>
public sealed record LeavePolicyRuleItem(string Name, Guid PolicyId, string PolicyName, IReadOnlyList<PolicyCriterion> Criteria);

/// <remarks>Version is null while the company has no rules yet; send it back when replacing.</remarks>
public sealed record LeavePolicyRules(Guid CompanyId, IReadOnlyList<LeavePolicyRuleItem> Rules, uint? Version);

/// <summary>The policy assignment rules of a company, in priority order (D41).</summary>
public sealed record GetLeavePolicyRulesQuery(Guid CompanyId) : IQuery<LeavePolicyRules>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Settings.Read;
}

/// <summary>
/// Replaces the policy assignment rules of a company; their order is their priority. Each rule's policy is active and
/// of every company or of this one (D41).
/// </summary>
public sealed record ReplaceLeavePolicyRulesCommand(uint? Version, IReadOnlyList<LeavePolicyRuleInput>? Rules) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid CompanyId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Settings.Manage;
}

/// <summary>
/// Gives an open employment a policy by hand, or with null lets the company's rules decide again (D41). The policy is
/// active and of every company or of the employment's.
/// </summary>
public sealed record SetEmploymentLeavePolicyCommand(uint Version, Guid? PolicyId) : ICommand, IRequirePermission
{
    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmployeeId { get; init; }

    /// <summary>From the route.</summary>
    [JsonIgnore]
    public Guid EmploymentId { get; init; }

    [JsonIgnore]
    public string Permission => HrPermissions.Leave.Settings.Manage;
}

/// <summary>Where an employment's policy comes from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LeavePolicySource>))]
public enum LeavePolicySource
{
    /// <summary>Set by hand on the employment.</summary>
    Override,

    /// <summary>The first matching rule of the company.</summary>
    Rule,

    /// <summary>The default policy of the company.</summary>
    CompanyDefault,

    /// <summary>The default policy of every company.</summary>
    TenantDefault,

    /// <summary>No policy applies.</summary>
    None,
}

/// <summary>
/// "Which policy applies to this employee?" (PLAN L1): the policy of an employment on a day, why, its version and the
/// annual leave it gives for the service completed and the age on that day (D41).
/// </summary>
public sealed record ResolveLeavePolicyQuery(Guid EmploymentId, DateOnly? Date = null) : IQuery<LeavePolicyResolution>, IRequirePermission
{
    public string Permission => HrPermissions.Leave.Settings.Read;
}

/// <remarks>
/// RulePosition is the matching rule's position from 1 when Source is Rule. VersionValidFrom is the first day of the
/// version in force, null for "from the beginning". CompletedYears are the years of service on the day from the
/// seniority date, Age the age on the day (null without a birth date), EntitlementDays the annual leave they give.
/// </remarks>
public sealed record LeavePolicyResolution(
    Guid EmploymentId,
    DateOnly Date,
    LeavePolicySource Source,
    Guid? PolicyId,
    string? PolicyCode,
    string? PolicyName,
    int? RulePosition,
    string? RuleName,
    DateOnly? VersionValidFrom,
    int CompletedYears,
    int? Age,
    decimal EntitlementDays);

internal sealed class ReplaceLeavePolicyRulesCommandValidator : AbstractValidator<ReplaceLeavePolicyRulesCommand>
{
    public ReplaceLeavePolicyRulesCommandValidator()
    {
        RuleFor(c => c.Rules).NotNull().Must(r => r!.Count <= LeavePolicyRuleSet.MaxRules).WithErrorCode(CommonErrors.ValidationCode);
        RuleForEach(c => c.Rules).ChildRules(rule =>
        {
            rule.RuleFor(r => r.Name).NotEmpty().MaximumLength(PolicyAssignmentRule.NameMaxLength);
            rule.RuleFor(r => r.PolicyId).NotEmpty();
            rule.RuleFor(r => r.Criteria).NotNull().Must(c => c!.Count <= PolicyAssignmentRule.MaxCriteria).WithErrorCode(CommonErrors.ValidationCode);
            rule.RuleForEach(r => r.Criteria).SetValidator(new PolicyCriterionValidator());
        });
    }
}

/// <summary>Which policies a company's employees may get, and the ids its criteria may name.</summary>
internal static class LeavePolicyAssignments
{
    /// <summary>An active policy of every company or of <paramref name="companyId"/>.</summary>
    public static bool Usable(LeavePolicy policy, Guid companyId) => policy.IsActive && (policy.CompanyId is null || policy.CompanyId == companyId);

    /// <summary>Criteria that name branches or units name those of the company.</summary>
    public static async Task<List<FieldError>> CheckCriteriaAsync(
        IHrDbContext db,
        IOrganizationLookup organization,
        Guid companyId,
        IReadOnlyList<LeavePolicyRuleInput> rules,
        CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        var ids = rules.SelectMany(r => r.Criteria ?? [])
            .Where(c => c.Kind is CriterionKind.Branch or CriterionKind.OrgUnit or CriterionKind.OrgUnitTree)
            .SelectMany(c => c.Values)
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var branches = (await organization.GetBranchesAsync(ids, cancellationToken)).Where(b => b.CompanyId == companyId).Select(b => b.Id).ToHashSet();
        var unitIds = ids.Select(OrgUnitId.From).ToList();
        var units = (await db.OrgUnits.AsNoTracking().Where(u => unitIds.Contains(u.Id) && u.CompanyId == companyId).Select(u => u.Id).ToListAsync(cancellationToken))
            .Select(u => u.Value)
            .ToHashSet();
        for (var r = 0; r < rules.Count; r++)
        {
            var criteria = rules[r].Criteria ?? [];
            for (var c = 0; c < criteria.Count; c++)
            {
                var known = criteria[c].Kind switch
                {
                    CriterionKind.Branch => branches,
                    CriterionKind.OrgUnit or CriterionKind.OrgUnitTree => units,
                    _ => null,
                };
                if (known is not null && criteria[c].Values.Any(v => !Guid.TryParse(v, out var id) || !known.Contains(id)))
                {
                    errors.Add(new FieldError($"rules[{r}].criteria[{c}].values", LeaveSettingsErrors.UnknownCriterionValueCode));
                }
            }
        }

        return errors;
    }

    /// <summary>The employment as the criteria see it on <paramref name="day"/>.</summary>
    public static async Task<PolicySubject> SubjectAsync(IHrDbContext db, Employee employee, Employment employment, DateOnly day, CancellationToken cancellationToken)
    {
        var assignment = employment.AssignmentOn(day) ?? employment.LatestAssignment;
        var positionId = assignment?.PositionId;
        var unit = positionId is { } id
            ? await db.Positions.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => db.OrgUnits.Where(u => u.Id == p.OrgUnitId).Select(u => new { u.Id, u.Path }).First())
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var path = unit?.Path.Split('.').Select(label => Guid.ParseExact(label, "N")).ToList() ?? [];
        return new PolicySubject(
            employment.CompanyId,
            assignment?.BranchId,
            employment.Type,
            unit?.Id.Value,
            path,
            assignment?.Level,
            employee.CustomFields.Values.ToDictionary(v => v.Key, v => Text(v.Value), StringComparer.Ordinal));
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };
}

internal sealed class GetLeavePolicyRulesQueryHandler(IHrDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser)
    : IQueryHandler<GetLeavePolicyRulesQuery, LeavePolicyRules>
{
    public async Task<Result<LeavePolicyRules>> Handle(GetLeavePolicyRulesQuery request, CancellationToken cancellationToken)
    {
        var readable = await LeaveSettings.ReadableAsync(scopes, cancellationToken);
        if (!readable(request.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        var set = await db.LeavePolicyRuleSets.AsNoTracking()
            .Where(s => s.CompanyId == request.CompanyId)
            .Select(s => new { Set = s, Version = EF.Property<uint>(s, ConcurrencyVersion.PropertyName) })
            .SingleOrDefaultAsync(cancellationToken);
        if (set is null)
        {
            return new LeavePolicyRules(request.CompanyId, [], null);
        }

        var policyIds = set.Set.Rules.Select(r => r.PolicyId).Distinct().ToList();
        var names = await db.LeavePolicies.AsNoTracking().Where(p => policyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        return new LeavePolicyRules(
            request.CompanyId,
            [.. set.Set.Rules.Select(r => new LeavePolicyRuleItem(
                r.Name,
                r.PolicyId.Value,
                names.TryGetValue(r.PolicyId, out var name) ? name.Get(currentUser.Culture, "tr-TR") : string.Empty,
                r.Criteria))],
            set.Version);
    }
}

internal sealed class ReplaceLeavePolicyRulesCommandHandler(IHrDbContext db, IAccessScopeProvider scopes, IOrganizationLookup organization)
    : ICommandHandler<ReplaceLeavePolicyRulesCommand>
{
    public async Task<Result> Handle(ReplaceLeavePolicyRulesCommand request, CancellationToken cancellationToken)
    {
        if (await LeaveSettings.CheckCompanyAsync(scopes, organization, request.CompanyId, cancellationToken) is { } companyError)
        {
            return companyError;
        }

        var rules = request.Rules!;
        var policyIds = rules.Select(r => LeavePolicyId.From(r.PolicyId)).Distinct().ToList();
        var policies = await db.LeavePolicies.AsNoTracking().Where(p => policyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var errors = rules
            .Select((rule, index) => (rule, index))
            .Where(x => !policies.TryGetValue(LeavePolicyId.From(x.rule.PolicyId), out var policy) || !LeavePolicyAssignments.Usable(policy, request.CompanyId))
            .Select(x => new FieldError($"rules[{x.index}].policyId", LeaveSettingsErrors.UnknownPolicyCode))
            .ToList();
        errors.AddRange(await LeavePolicyAssignments.CheckCriteriaAsync(db, organization, request.CompanyId, rules, cancellationToken));
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var set = await db.LeavePolicyRuleSets.SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);
        if (set is null)
        {
            set = LeavePolicyRuleSet.Create(request.CompanyId);
            db.LeavePolicyRuleSets.Add(set);
        }
        else
        {
            db.Entry(set).ExpectVersion(request.Version ?? 0);
        }

        set.Replace([.. rules.Select(r => new PolicyAssignmentRule(
            r.Name!.Trim(),
            LeavePolicyId.From(r.PolicyId),
            [.. r.Criteria!.Select(c => c with { Values = [.. c.Values.Select(v => v.Trim())], FieldCode = c.FieldCode?.Trim() })]))]);
        return Result.Success();
    }
}

internal sealed class SetEmploymentLeavePolicyCommandHandler(IHrDbContext db, IAccessScopeProvider scopes) : ICommandHandler<SetEmploymentLeavePolicyCommand>
{
    public async Task<Result> Handle(SetEmploymentLeavePolicyCommand request, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, request.Permission, cancellationToken);
        var employee = await EmployeeRules.LoadAsync(db, request.EmployeeId, cancellationToken);
        var employment = employee?.Employments.SingleOrDefault(m => m.Id == EmploymentId.From(request.EmploymentId));
        if (employee is null || employment is null || !scope.CoversCompany(employment.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        if (request.PolicyId is { } policyId)
        {
            var policy = await db.LeavePolicies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == LeavePolicyId.From(policyId), cancellationToken);
            if (policy is null || !LeavePolicyAssignments.Usable(policy, employment.CompanyId))
            {
                return Error.Validation([new FieldError("policyId", LeaveSettingsErrors.UnknownPolicyCode)]);
            }
        }

        db.Entry(employee).ExpectVersion(request.Version);
        return employee.SetLeavePolicy(employment.Id, request.PolicyId);
    }
}

/// <summary>An employment's policy and why (D41).</summary>
public sealed record ChosenLeavePolicy(LeavePolicy? Policy, LeavePolicySource Source, PolicyAssignmentRule? Rule, int? RulePosition, PolicySubject Subject);

/// <summary>
/// Chooses an employment's policy on a day (D41): the override, else the company's first matching rule, else the
/// company's default, else every company's. The accrual job and the policy resolution query share it.
/// </summary>
public sealed class LeavePolicyResolver(IHrDbContext db)
{
    public async Task<ChosenLeavePolicy> ChooseAsync(Employee employee, Employment employment, DateOnly day, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(employment);
        var subject = await LeavePolicyAssignments.SubjectAsync(db, employee, employment, day, cancellationToken);
        var policies = await db.LeavePolicies.AsNoTracking()
            .Where(p => p.CompanyId == null || p.CompanyId == employment.CompanyId)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        if (employment.LeavePolicyId is { } overrideId && policies.TryGetValue(LeavePolicyId.From(overrideId), out var chosen))
        {
            return new ChosenLeavePolicy(chosen, LeavePolicySource.Override, null, null, subject);
        }

        var set = await db.LeavePolicyRuleSets.AsNoTracking().SingleOrDefaultAsync(s => s.CompanyId == employment.CompanyId, cancellationToken);
        if (set?.FirstMatch(subject, id => policies.TryGetValue(id, out var p) && LeavePolicyAssignments.Usable(p, employment.CompanyId)) is { } match)
        {
            return new ChosenLeavePolicy(policies[match.Rule.PolicyId], LeavePolicySource.Rule, match.Rule, match.Position, subject);
        }

        var defaults = policies.Values.Where(p => p.IsDefault && p.IsActive).ToList();
        return defaults.SingleOrDefault(p => p.CompanyId == employment.CompanyId) is { } companyDefault
            ? new ChosenLeavePolicy(companyDefault, LeavePolicySource.CompanyDefault, null, null, subject)
            : defaults.SingleOrDefault(p => p.CompanyId is null) is { } tenantDefault
                ? new ChosenLeavePolicy(tenantDefault, LeavePolicySource.TenantDefault, null, null, subject)
                : new ChosenLeavePolicy(null, LeavePolicySource.None, null, null, subject);
    }
}

internal sealed class ResolveLeavePolicyQueryHandler(
    IHrDbContext db,
    IAccessScopeProvider scopes,
    IOrganizationLookup organization,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    LeavePolicyResolver resolver) : IQueryHandler<ResolveLeavePolicyQuery, LeavePolicyResolution>
{
    public async Task<Result<LeavePolicyResolution>> Handle(ResolveLeavePolicyQuery request, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.Employments)
            .ThenInclude(m => m.Assignments)
            .SingleOrDefaultAsync(e => e.Employments.Any(m => m.Id == EmploymentId.From(request.EmploymentId)), cancellationToken);
        var employment = employee?.Employments.Single(m => m.Id == EmploymentId.From(request.EmploymentId));
        var readable = await LeaveSettings.ReadableAsync(scopes, cancellationToken);
        if (employee is null || employment is null || !readable(employment.CompanyId))
        {
            return CommonErrors.NotFound;
        }

        var day = request.Date ?? await LeaveTypeRules.TodayAsync(organization, timeProvider, employment.CompanyId, cancellationToken);
        var chosen = await resolver.ChooseAsync(employee, employment, day, cancellationToken);
        var policy = chosen.Policy;
        var years = LeaveAccruals.CompletedYears(employment.SeniorityDate, day);
        int? age = employee.BirthDate is { } birth ? LeaveAccruals.CompletedYears(birth, day) : null;
        var version = policy?.VersionOn(day);
        return new LeavePolicyResolution(
            employment.Id.Value,
            day,
            chosen.Source,
            policy?.Id.Value,
            policy?.Code,
            policy?.Name.Get(currentUser.Culture, "tr-TR"),
            chosen.RulePosition,
            chosen.Rule?.Name,
            version is null || version.ValidFrom == DateOnly.MinValue ? null : version.ValidFrom,
            years,
            age,
            version?.Rules.Entitlement(years, age, c => c.Matches(chosen.Subject)) ?? 0);
    }
}
