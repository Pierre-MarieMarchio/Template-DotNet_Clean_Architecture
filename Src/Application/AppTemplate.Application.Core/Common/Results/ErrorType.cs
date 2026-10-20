namespace AppTemplate.Application.Core.Common.Results;

public enum ErrorType
{
    Validation,

    NotFound,

    Unauthorized,

    Forbidden,

    Conflict,

    TooManyRequests,

    PreconditionFailed,

    PreconditionRequired,
}
