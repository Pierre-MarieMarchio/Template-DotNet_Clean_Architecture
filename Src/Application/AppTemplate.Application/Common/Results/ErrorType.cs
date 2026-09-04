namespace AppTemplate.Application.Common.Results;

/// <summary>How a failure should be surfaced at the transport boundary.</summary>
public enum ErrorType
{
    /// <summary>Maps to HTTP 400 / 422.</summary>
    Validation,

    /// <summary>Maps to HTTP 404.</summary>
    NotFound,

    /// <summary>The caller is not authenticated. Maps to HTTP 401.</summary>
    Unauthorized,

    /// <summary>The caller is authenticated but not allowed. Maps to HTTP 403.</summary>
    Forbidden,

    /// <summary>State prevents the operation, e.g. a duplicate. Maps to HTTP 409.</summary>
    Conflict,

    /// <summary>Maps to HTTP 429.</summary>
    TooManyRequests,

    /// <summary>
    /// A condition the caller attached to the request does not hold against the current state.
    /// Maps to HTTP 412.
    /// </summary>
    PreconditionFailed,

    /// <summary>
    /// The operation may only be performed conditionally, and the caller attached no condition.
    /// Maps to HTTP 428.
    /// </summary>
    PreconditionRequired,
}
