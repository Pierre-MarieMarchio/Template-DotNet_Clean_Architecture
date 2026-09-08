using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// The narrowing every use case does before it touches a caller's own data, so an anonymous request
/// is a failure rather than a null each use case remembers to test for.
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>The caller's id, or a failure when the request is anonymous.</summary>
    public static Result<UserId> RequireUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        return currentUser.UserId is { } userId
            ? userId
            : Result.Failure<UserId>(CommonErrors.NotAuthenticated);
    }
}
