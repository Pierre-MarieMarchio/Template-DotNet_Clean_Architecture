namespace AppTemplate.Application.Core.Common.Results;

/// <summary>Failures no single vertical owns.</summary>
public static class CommonErrors
{
    /// <summary>
    /// What a use case reports when it needs to know who is asking and the request does not say.
    /// </summary>
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
