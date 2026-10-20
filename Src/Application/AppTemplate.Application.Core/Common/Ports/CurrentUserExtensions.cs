using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ports;

public static class CurrentUserExtensions
{
    public static Result<UserId> RequireUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        return currentUser.UserId is { } userId
            ? userId
            : Result.Failure<UserId>(CommonErrors.NotAuthenticated);
    }
}
