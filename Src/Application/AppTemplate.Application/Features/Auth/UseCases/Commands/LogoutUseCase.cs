using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.Validators;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

public sealed record LogoutRequest(string RefreshToken);

/// <summary>Idempotent, and never reveals whether the token existed.</summary>
public interface ILogoutUseCase : IUseCase<LogoutRequest, Result>;

public sealed class LogoutUseCase(
    IRefreshTokenGrants refreshTokens,
    IValidator<LogoutRequest> validator) : ILogoutUseCase
{
    public async Task<Result> ExecuteAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken);

        if (!validation.IsValid)
        {
            return Result.Failure(validation.ToError());
        }

        await refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);

        // Success even for a token nobody was issued, so signing out cannot be used to test one.
        return Result.Success();
    }
}
