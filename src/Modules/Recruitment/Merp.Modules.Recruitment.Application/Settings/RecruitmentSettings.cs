using System.Linq.Expressions;
using FluentValidation;
using FluentValidation.Results;
using Merp.Application.Abstractions.Localization;
using Merp.Application.Abstractions.Notifications;
using Merp.Application.Abstractions.Security;
using Merp.Application.Abstractions.Settings;
using Merp.Application.Abstractions.Validation;
using Merp.SharedKernel.Notifications;
using Merp.SharedKernel.Results;
using Merp.SharedKernel.ValueObjects;

namespace Merp.Modules.Recruitment.Application.Settings;

/// <summary>Settings of recruitment (D47); texts are <c>recruitment.settings.*</c>.</summary>
public sealed class RecruitmentSettingDefinitions : ISettingDefinitionProvider
{
    /// <summary>
    /// Months after a candidate's last application closed that their data is kept; then they are anonymized (R8). A
    /// company may keep its own.
    /// </summary>
    public const string RetentionMonths = "recruitment.retention_months";

    public const int DefaultRetentionMonths = 24;

    /// <summary>
    /// An interviewer sees the other forms of an application only after sending their own (D52); HR and the hiring
    /// manager always see them. A company may keep its own.
    /// </summary>
    public const string BlindFeedback = "recruitment.blind_feedback";

    public IEnumerable<SettingDefinition> GetSettings() =>
    [
        SettingDefinition.Boolean(
            BlindFeedback,
            true,
            "recruitment.settings.blind_feedback",
            "recruitment.settings.blind_feedback_hint",
            companyOverridable: true),
        SettingDefinition.Number(
            RetentionMonths,
            DefaultRetentionMonths,
            "recruitment.settings.retention_months",
            "recruitment.settings.retention_months_hint",
            companyOverridable: true,
            min: 1,
            max: 120,
            wholeNumber: true),
    ];
}

/// <summary>
/// The e-mails candidates get (D47): external notification types, sent to the candidate's address. Their subject and
/// text are <c>recruitment.notifications.{name}.title/body</c>; tenants replace them on the notification templates
/// screen, by company and language.
/// </summary>
public sealed class RecruitmentNotificationTypes : INotificationTypeProvider
{
    public const string ApplicationReceived = "recruitment.application_received";
    public const string InterviewInvitation = "recruitment.interview_invitation";
    public const string ApplicationRejected = "recruitment.application_rejected";
    public const string OfferSent = "recruitment.offer_sent";
    public const string InterviewUpdated = "recruitment.interview_updated";
    public const string InterviewCancelled = "recruitment.interview_cancelled";
    public const string InterviewReminder = "recruitment.interview_reminder";

    public IEnumerable<NotificationTypeDefinition> GetNotificationTypes() =>
    [
        new(ApplicationReceived, [NotificationChannel.Email], External: true),
        new(InterviewInvitation, [NotificationChannel.Email], External: true),
        new(ApplicationRejected, [NotificationChannel.Email], External: true),
        new(OfferSent, [NotificationChannel.Email], External: true),
        new(InterviewUpdated, [NotificationChannel.Email], External: true),
        new(InterviewCancelled, [NotificationChannel.Email], External: true),
        new(InterviewReminder, [NotificationChannel.Email], External: true),
    ];
}

public static class RecruitmentSettingsErrors
{
    /// <summary>The tenant's recruitment catalogs (stages, reasons, sources, tags) are changed with the Tenant scope.</summary>
    public static readonly Error TenantScopeRequired = Error.Forbidden("recruitment.settings.errors.tenant_scope_required");

    public const string DuplicateCodeCode = "recruitment.settings.errors.duplicate_code";
}

/// <summary>Checks shared by the recruitment settings.</summary>
internal static class RecruitmentSettingRules
{
    /// <summary>Whether the caller holds <paramref name="permission"/> with the Tenant scope.</summary>
    public static async Task<bool> IsTenantWideAsync(IAccessScopeProvider scopes, string permission, CancellationToken cancellationToken) =>
        (await scopes.GetCurrentUserScopesAsync(permission, cancellationToken)).Any(s => s.Type == AccessScopeTypes.Tenant);

    /// <summary>The companies the caller reaches with <paramref name="permission"/>; null for every company.</summary>
    public static async Task<IReadOnlySet<Guid>?> CompaniesAsync(IAccessScopeProvider scopes, string permission, CancellationToken cancellationToken)
    {
        var list = await scopes.GetCurrentUserScopesAsync(permission, cancellationToken);
        return list.Any(s => s.Type == AccessScopeTypes.Tenant)
            ? null
            : list.Where(s => s.Type == AccessScopeTypes.Company && s.ScopeId is not null).Select(s => s.ScopeId!.Value).ToHashSet();
    }

    public static void AddNameRules<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, IReadOnlyDictionary<string, string?>?>> name,
        ILocalizationSource localization,
        int maxLength,
        string field = "Name")
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
                context.AddFailure(new ValidationFailure(field, result.Error.Code) { ErrorCode = result.Error.Code });
            }
            else if (result.Value.Values.Values.Any(text => text.Length > maxLength))
            {
                context.AddFailure(new ValidationFailure(field, CommonErrors.MaxLength(maxLength).Code)
                {
                    ErrorCode = CommonErrors.MaxLength(maxLength).Code,
                    FormattedMessagePlaceholderValues = new Dictionary<string, object> { ["MaxLength"] = maxLength },
                });
            }
        });
    }
}
