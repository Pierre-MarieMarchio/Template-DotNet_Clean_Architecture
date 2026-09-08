using AppTemplate.Application.Auth.Features.Auth.Ports.UserProfiles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.GetCurrentUser;

/// <summary>
/// Reads the profile from the store. A caller whose account has since been deleted is answered as
/// unauthenticated rather than as missing: their token is still valid and there is nothing behind
/// it any more.
/// </summary>
public sealed class GetCurrentUserUseCase(
    IUserProfilesService profiles,
    ICurrentUser currentUser) : IGetCurrentUserUseCase
{
    /// <inheritdoc />
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
