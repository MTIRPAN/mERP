using Microsoft.AspNetCore.Http;

namespace Merp.Web.Errors;

/// <summary>Localization keys for HTTP statuses: a title per status and a default error code.</summary>
internal static class ProblemDetailsCodes
{
    public static string TitleKey(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "common.http.bad_request",
        StatusCodes.Status401Unauthorized => "common.http.unauthorized",
        StatusCodes.Status403Forbidden => "common.http.forbidden",
        StatusCodes.Status404NotFound => "common.http.not_found",
        StatusCodes.Status405MethodNotAllowed => "common.http.method_not_allowed",
        StatusCodes.Status409Conflict => "common.http.conflict",
        StatusCodes.Status413PayloadTooLarge => "common.http.payload_too_large",
        StatusCodes.Status415UnsupportedMediaType => "common.http.unsupported_media_type",
        StatusCodes.Status429TooManyRequests => "common.http.too_many_requests",
        StatusCodes.Status503ServiceUnavailable => "common.http.service_unavailable",
        >= 500 => "common.http.internal_server_error",
        _ => "common.http.error",
    };

    public static string DefaultErrorCode(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "common.errors.invalid_request",
        StatusCodes.Status401Unauthorized => "common.errors.unauthorized",
        StatusCodes.Status403Forbidden => "common.errors.forbidden",
        StatusCodes.Status404NotFound => "common.errors.not_found",
        StatusCodes.Status405MethodNotAllowed => "common.errors.method_not_allowed",
        StatusCodes.Status409Conflict => "common.errors.concurrency_conflict",
        StatusCodes.Status413PayloadTooLarge => "common.errors.payload_too_large",
        StatusCodes.Status415UnsupportedMediaType => "common.errors.unsupported_media_type",
        StatusCodes.Status429TooManyRequests => "common.errors.too_many_requests",
        StatusCodes.Status503ServiceUnavailable => "common.errors.service_unavailable",
        _ => "common.errors.unexpected",
    };
}
