using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;

public sealed class RevokeSessionUseCase(
    ISessionsService sessions,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<RevokeSessionCommand> validator) : IRevokeSessionUseCase
{
    public async Task<Result> ExecuteAsync(RevokeSessionCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId;
        }

        if (!await sessions.RevokeAsync(userId.Value.Value, request.SessionId, cancellationToken))
        {
            return Result.Failure(SessionsErrors.NoSuchSession);
        }

        securityEventLog.Record(SecurityEvent.RefreshTokenRevoked(userId.Value.Value));

        return Result.Success();
    }
}
