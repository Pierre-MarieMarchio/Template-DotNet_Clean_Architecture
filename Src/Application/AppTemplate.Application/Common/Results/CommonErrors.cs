namespace AppTemplate.Application.Common.Results;

/// <summary>Failures no single vertical owns.</summary>
public static class CommonErrors
{
    public static readonly Error NotAuthenticated = Error.Unauthorized(
        "auth.required",
        "This operation requires an authenticated user.");

    /// <summary>
    /// <paramref name="message"/> is the <c>DomainException</c> text, returned to the client
    /// verbatim: only pass messages the aggregate authored, never a provider's.
    /// </summary>
    public static Error InvariantViolated(string message) => Error.Conflict(
        "domain.invariantViolated",
        message);
}
