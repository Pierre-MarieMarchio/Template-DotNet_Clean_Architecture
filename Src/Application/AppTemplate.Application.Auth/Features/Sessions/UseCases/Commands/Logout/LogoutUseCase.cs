using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;

public sealed class LogoutUseCase(
    ISessionTokensService sessionTokens,
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

        var userId = await sessionTokens.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);

        if (userId is { } id)
        {
            securityEventLog.Record(SecurityEvent.LoggedOut(id));
        }

        return Result.Success();
    }
}
