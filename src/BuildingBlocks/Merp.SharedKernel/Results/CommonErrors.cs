namespace Merp.SharedKernel.Results;

/// <summary>Errors shared by all modules. Texts live in <c>Localization/tr-TR.json</c>.</summary>
public static class CommonErrors
{
    public const string ValidationCode = "common.errors.validation";

    public static readonly Error NotFound = Error.NotFound("common.errors.not_found");
    public static readonly Error Forbidden = Error.Forbidden("common.errors.forbidden");
    public static readonly Error ConcurrencyConflict = Error.Conflict("common.errors.concurrency_conflict");
    public static readonly Error Unexpected = Error.Failure("common.errors.unexpected");

    public static readonly Error Required = Error.Validation("common.errors.required");
    public static readonly Error InvalidEmail = Error.Validation("common.errors.invalid_email");
    public static readonly Error InvalidPhoneNumber = Error.Validation("common.errors.invalid_phone_number");
    public static readonly Error InvalidTckn = Error.Validation("common.errors.invalid_tckn");
    public static readonly Error InvalidVkn = Error.Validation("common.errors.invalid_vkn");
    public static readonly Error InvalidIban = Error.Validation("common.errors.invalid_iban");
    public static readonly Error InvalidDateRange = Error.Validation("common.errors.invalid_date_range");
    public static readonly Error InvalidPostalCode = Error.Validation("common.errors.invalid_postal_code");
    public static readonly Error InvalidCountryCode = Error.Validation("common.errors.invalid_country_code");
    public static readonly Error InvalidCulture = Error.Validation("common.errors.invalid_culture");
    public static readonly Error InvalidCode = Error.Validation("common.errors.invalid_code");
    public static readonly Error InvalidMersisNumber = Error.Validation("common.errors.invalid_mersis_number");
    public static readonly Error InvalidSgkWorkplaceNumber = Error.Validation("common.errors.invalid_sgk_workplace_number");
    public static readonly Error InvalidTimeZone = Error.Validation("common.errors.invalid_time_zone");
    public static readonly Error InvalidSort = Error.Validation("common.errors.invalid_sort");
    public static readonly Error Duplicate = Error.Validation("common.errors.duplicate");

    public static Error MaxLength(int max) =>
        Error.Validation("common.errors.max_length", new Dictionary<string, object?> { ["maxLength"] = max });
}
