namespace AppTemplate.Application.Core.Common.Results;

public static class CommonErrors
{
    public static readonly Error NotAuthenticated = Error.Unauthorized(
        "auth.required",
        "This operation requires an authenticated user.");

    public static Error InvariantViolated(string message) => Error.Conflict(
        "domain.invariantViolated",
        message);
}
