using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Logout;

/// <summary>
/// Revokes the presented grant and succeeds either way, so a caller cannot use sign-out to find
/// out whether a token was live.
/// </summary>
public sealed class LogoutUseCase(
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    IValidator<LogoutCommand> validator) : ILogoutUseCase
{
    /// <inheritdoc />
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
