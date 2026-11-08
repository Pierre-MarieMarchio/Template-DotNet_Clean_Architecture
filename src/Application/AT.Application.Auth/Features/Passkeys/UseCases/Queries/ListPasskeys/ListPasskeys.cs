using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Queries.ListPasskeys;

public sealed record ListPasskeysQuery : IQuery<Result<IReadOnlyList<PasskeyCredential>>>;

public sealed class ListPasskeysQueryHandler(
    IPasskeyService passkeys,
    ICurrentUser currentUser) : IQueryHandler<ListPasskeysQuery, Result<IReadOnlyList<PasskeyCredential>>>
{
    public async ValueTask<Result<IReadOnlyList<PasskeyCredential>>> Handle(
        ListPasskeysQuery query,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId.To<IReadOnlyList<PasskeyCredential>>();
        }

        return Result.Success(
            await passkeys.ListAsync(callerId.Value.Value, cancellationToken));
    }
}
