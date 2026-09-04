using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Dtos;
using AppTemplate.Application.Features.Auth.Ports;

namespace AppTemplate.Application.Features.Auth.UseCases.Queries;

/// <summary>Whole input is ambient: the caller's own id, taken from the request's principal.</summary>
public interface IGetCurrentUserUseCase : IUseCase<Result<CurrentUserResponse>>;

public sealed class GetCurrentUserUseCase(
    IUserProfiles profiles,
    ICurrentUser currentUser) : IGetCurrentUserUseCase
{
    public async Task<Result<CurrentUserResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<CurrentUserResponse>();
        }

        // Read from the store rather than from the principal's claims — see IUserProfiles for why a
        // claim-built profile would be stale.
        var profile = await profiles.FindByIdAsync(userId.Value, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CurrentUserResponse>(CommonErrors.NotAuthenticated);
        }

        return Result.Success(new CurrentUserResponse(
            profile.UserId,
            profile.UserName,
            profile.Email,
            profile.EmailConfirmed,
            profile.Roles,
            profile.CreatedAt,
            profile.TwoFactorEnabled));
    }
}
