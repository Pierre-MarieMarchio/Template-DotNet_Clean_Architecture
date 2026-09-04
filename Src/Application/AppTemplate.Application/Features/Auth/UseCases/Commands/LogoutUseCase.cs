using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Ports;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

public sealed record LogoutCommand(string RefreshToken);

/// <summary>Idempotent, and never reveals whether the token existed.</summary>
public interface ILogoutUseCase : IUseCase<LogoutCommand, Result>;

public sealed class LogoutUseCase(
    IRefreshTokenGrants refreshTokens,
    ISecurityEventLog securityEventLog,
    IValidator<LogoutCommand> validator) : ILogoutUseCase
{
    public async Task<Result> ExecuteAsync(LogoutCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var userId = await refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);

        if (userId is { } id)
        {
            securityEventLog.Record(SecurityEvent.LoggedOut(id));
        }

        // Success even for a token nobody was issued, so signing out cannot be used to test one.
        return Result.Success();
    }
}
