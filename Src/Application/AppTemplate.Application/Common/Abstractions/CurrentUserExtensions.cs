namespace AppTemplate.Application.Common.Abstractions;

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
