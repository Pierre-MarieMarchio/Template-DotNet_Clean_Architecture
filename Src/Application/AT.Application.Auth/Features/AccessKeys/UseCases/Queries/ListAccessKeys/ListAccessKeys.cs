using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Queries.ListAccessKeys;

public sealed record ListAccessKeysQuery : IQuery<Result<IReadOnlyList<LiveAccessKey>>>;

public sealed class ListAccessKeysQueryHandler(
    IAccessKeysService accessKeys,
    ICurrentUser currentUser) : IQueryHandler<ListAccessKeysQuery, Result<IReadOnlyList<LiveAccessKey>>>
{
    public async ValueTask<Result<IReadOnlyList<LiveAccessKey>>> Handle(ListAccessKeysQuery query, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<IReadOnlyList<LiveAccessKey>>();
        }

        return Result.Success(await accessKeys.ListLiveAsync(callerId.Value.Value, cancellationToken));
    }
}
