using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.UserProfiles;

namespace AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser;

public sealed class GetCurrentUserUseCase(
    IUserProfilesService profiles,
    ICurrentUser currentUser) : IGetCurrentUserUseCase
{
    public async Task<Result<GetCurrentUserOutcome>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<GetCurrentUserOutcome>();
        }

        // Read from the store rather than from the principal's claims — see IUserProfilesService for why a
        // claim-built profile would be stale.
        var profile = await profiles.FindByIdAsync(userId.Value, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<GetCurrentUserOutcome>(CommonErrors.NotAuthenticated);
        }

        return Result.Success(new GetCurrentUserOutcome(
            profile.UserId,
            profile.UserName,
            profile.Email,
            profile.EmailConfirmed,
            profile.Roles,
            profile.CreatedAt,
            profile.TwoFactorEnabled));
    }
}
