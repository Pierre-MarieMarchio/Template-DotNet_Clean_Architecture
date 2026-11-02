using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.GetCurrentUser;

public sealed class GetCurrentUserUseCase(
    IAccountsService accounts,
    ICurrentUser currentUser) : IGetCurrentUserUseCase
{
    public async Task<Result<GetCurrentUserOutcome>> ExecuteAsync(CancellationToken cancellationToken = default)
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
