namespace Merp.SharedKernel.Results;

/// <summary>Category of an expected business error. The API layer maps it to an HTTP status code.</summary>
public enum ErrorType
{
    /// <summary>400 — input is invalid.</summary>
    Validation,

    /// <summary>404 — the record does not exist or is outside the caller's data scope.</summary>
    NotFound,

    /// <summary>409 — state conflict (duplicate, concurrent update, invalid state transition).</summary>
    Conflict,

    /// <summary>403 — the caller lacks the required permission.</summary>
    Forbidden,

    /// <summary>500 — an expected but unrecoverable failure.</summary>
    Failure,

    /// <summary>503 — an external dependency (e.g. the AI provider) is temporarily unavailable; retrying may help.</summary>
    Unavailable,
}
