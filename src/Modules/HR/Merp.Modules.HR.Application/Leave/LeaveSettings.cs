using System.Linq.Expressions;
using FluentValidation;
using FluentValidation.Results;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Validation;
using Merp.Modules.HR.Application.Organization;
using Merp.Modules.HR.Domain.Leave;
using Merp.Modules.Platform.Contracts.Organization;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.HR.Application.Leave;

/// <summary>A legal minimum warning in the caller's language (D41).</summary>
/// <param name="Code">The warning's key.</param>
/// <param name="Message">Its text.</param>
/// <param name="ValidFrom">The version it was found in.</param>
public sealed record LeaveLegalWarning(string Code, string Message, DateOnly? ValidFrom);

public static class LeaveSettingsErrors
{
    public const string UnknownPolicyCode = "hr.leave.errors.unknown_policy";
    public const string UnknownLeaveTypeCode = "hr.leave.errors.unknown_leave_type";
    public const string FieldCodeRequiredCode = "hr.leave.errors.field_code_required";
    public const string UnknownCriterionValueCode = "hr.leave.errors.unknown_criterion_value";
    public const string BeforeEventAboveFixedCode = "hr.leave.errors.days_before_event_above_fixed";

    /// <summary>Records of every company need the Tenant scope, like the job catalog (D37, D41).</summary>
    public static readonly Error TenantScopeRequired = Error.Forbidden("hr.leave.errors.tenant_scope_required");
}

/// <summary>Checks shared by leave types, policies and their rules (D41).</summary>
internal static class LeaveSettings
{
    public const int MaxDays = 999;
    public const int MaxVersions = 20;
    public const int MaxValueLength = 100;

    /// <summary>
    /// Whether the caller may change a record of <paramref name="companyId"/> (null: every company, Tenant scope) and,
    /// with <paramref name="requireActive"/>, whether that company exists and is active.
    /// </summary>
    public static async Task<Error?> CheckCompanyAsync(
        IAccessScopeProvider scopes,
        IOrganizationLookup organization,
        Guid? companyId,
        CancellationToken cancellationToken,
        bool requireActive = true)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, HrPermissions.Leave.Settings.Manage, cancellationToken);
        if (companyId is not { } id)
        {
            return scope.IsTenantWide ? null : LeaveSettingsErrors.TenantScopeRequired;
        }

        if (!scope.CoversCompany(id))
        {
            return CommonErrors.Forbidden;
        }

        return requireActive && await OrgReferences.CheckCompanyAsync(organization, id, cancellationToken) is { } error
            ? Error.Validation([error])
            : null;
    }

    /// <summary>The records of every company and of the companies the caller reads.</summary>
    public static async Task<Func<Guid?, bool>> ReadableAsync(IAccessScopeProvider scopes, CancellationToken cancellationToken)
    {
        var scope = await OrgScope.ForCompaniesAsync(scopes, HrPermissions.Leave.Settings.Read, cancellationToken);
        return companyId => companyId is not { } id || scope.CoversCompany(id);
    }

    public static IReadOnlyList<LeaveLegalWarning> Localize(IEnumerable<LegalMinimumViolation> violations, ITextLocalizer localizer) =>
        [.. violations.Select(v => new LeaveLegalWarning(v.Code, localizer.Get(v.Code, v.Args), v.ValidFrom == DateOnly.MinValue ? null : v.ValidFrom))];

    public static void AddNameRules<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, IReadOnlyDictionary<string, string?>?>> name,
        ILocalizationSource localization,
        int maxLength)
    {
        validator.RuleFor(name).NotNull().MustCoverDefaultCulture(localization);
        validator.RuleFor(name).Custom((value, context) =>
        {
            if (value is null)
            {
                return;
            }

            var result = LocalizedString.Create(value);
            if (result.IsFailure)
            {
                context.AddFailure(new ValidationFailure("Name", result.Error.Code) { ErrorCode = result.Error.Code });
            }
            else if (result.Value.Values.Values.Any(text => text.Length > maxLength))
            {
                context.AddFailure(new ValidationFailure("Name", CommonErrors.MaxLength(maxLength).Code)
                {
                    ErrorCode = CommonErrors.MaxLength(maxLength).Code,
                    FormattedMessagePlaceholderValues = new Dictionary<string, object> { ["MaxLength"] = maxLength },
                });
            }
        });
    }
}

/// <summary>A criterion names its values; a custom field criterion its field.</summary>
internal sealed class PolicyCriterionValidator : AbstractValidator<PolicyCriterion>
{
    public PolicyCriterionValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Values).NotEmpty().Must(v => v.Count <= PolicyCriterion.MaxValues).WithErrorCode(CommonErrors.ValidationCode);
        RuleForEach(c => c.Values).NotEmpty().MaximumLength(LeaveSettings.MaxValueLength);
        RuleFor(c => c.FieldCode).NotEmpty().WithErrorCode(LeaveSettingsErrors.FieldCodeRequiredCode).When(c => c.Kind == CriterionKind.CustomField);
        RuleFor(c => c.FieldCode).MaximumLength(LeaveSettings.MaxValueLength);
    }
}
