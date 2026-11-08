using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Accounts.Model.Policies;

public static class SelfAdministrationPolicy
{
    public static Result EnsureNotSelf(Guid callerId, Guid targetId, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return callerId == targetId ? Result.Failure(error) : Result.Success();
    }
}
