using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// The narrowing every use case does before it touches a caller's own data, so an anonymous request
/// is a failure rather than a null each use case remembers to test for.
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>The caller's id, or a failure when the request is anonymous.</summary>
    public static Result<Guid> RequireUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        // Result<Guid> has no null to refuse the way Success(TValue) refuses null, so an empty id
        // — which no real authenticated caller carries — is checked here explicitly.
        return currentUser.UserId is { } userId && userId != Guid.Empty
            ? userId
            : Result.Failure<Guid>(CommonErrors.NotAuthenticated);
    }
}
