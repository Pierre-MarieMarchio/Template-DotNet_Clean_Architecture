using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery : IQuery<Result<GetCurrentUserOutcome>>;

public sealed class GetCurrentUserQueryHandler(
    IAccountsService accounts,
    ICurrentUser currentUser) : IQueryHandler<GetCurrentUserQuery, Result<GetCurrentUserOutcome>>
{
    public async ValueTask<Result<GetCurrentUserOutcome>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<GetCurrentUserOutcome>();
        }

        var profile = await accounts.FindProfileAsync(userId.Value.Value, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<GetCurrentUserOutcome>(CommonErrors.NotAuthenticated);
        }

        return Result.Success(new GetCurrentUserOutcome(
            profile.UserId,
            profile.Email,
            profile.EmailConfirmed,
            profile.Roles,
            profile.CreatedAt,
            profile.TwoFactorEnabled));
    }
}
