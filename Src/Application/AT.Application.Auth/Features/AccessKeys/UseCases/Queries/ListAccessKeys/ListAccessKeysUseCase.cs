using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Queries.ListAccessKeys;

public sealed class ListAccessKeysUseCase(
    IAccessKeysService accessKeys,
    ICurrentUser currentUser) : IListAccessKeysUseCase
{
    public async Task<Result<IReadOnlyList<LiveAccessKey>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<IReadOnlyList<LiveAccessKey>>();
        }

        return Result.Success(await accessKeys.ListLiveAsync(callerId.Value.Value, cancellationToken));
    }
}
