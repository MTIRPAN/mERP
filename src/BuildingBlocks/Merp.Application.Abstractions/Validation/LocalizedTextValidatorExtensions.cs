using FluentValidation;
using FluentValidation.Results;
using Merp.Application.Abstractions.Localization;

namespace Merp.Application.Abstractions.Validation;

public static class LocalizedTextValidatorExtensions
{
    public const string DefaultCultureRequiredCode = "common.errors.default_culture_required";
    public const string InactiveCultureCode = "common.errors.inactive_culture";

    /// <summary>
    /// Master data text in several cultures (<c>LocalizedString</c> input, D11): the tenant's default culture must have
    /// a text and every text must be in an active culture. A null input is left to other rules.
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, IReadOnlyDictionary<string, string?>?> MustCoverDefaultCulture<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, string?>?> ruleBuilder,
        ILocalizationSource localization)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        ArgumentNullException.ThrowIfNull(localization);

        return ruleBuilder.CustomAsync(async (texts, context, cancellationToken) =>
        {
            if (texts is null)
            {
                return;
            }

            var snapshot = await localization.GetAsync(cancellationToken);
            var given = texts.Where(t => !string.IsNullOrWhiteSpace(t.Value)).Select(t => t.Key).ToList();
            if (given.FirstOrDefault(culture => !snapshot.IsActive(culture)) is { } inactive)
            {
                context.AddFailure(Failure(context.PropertyPath, InactiveCultureCode, inactive));
            }
            else if (given.Count > 0 && !given.Contains(snapshot.DefaultCulture, StringComparer.OrdinalIgnoreCase))
            {
                context.AddFailure(Failure(context.PropertyPath, DefaultCultureRequiredCode, snapshot.DefaultCulture));
            }
        });
    }

    private static ValidationFailure Failure(string property, string code, string culture) => new(property, code)
    {
        ErrorCode = code,
        FormattedMessagePlaceholderValues = new Dictionary<string, object> { ["culture"] = culture },
    };
}
