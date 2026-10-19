using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;

/// <summary>
/// Ends one of the caller's sessions. Somebody else's session and an unknown id are the same 404,
/// so the route cannot be used to learn which session ids exist. A session already ended succeeds
/// again: the outcome the caller asked for holds either way.
/// <para>
/// Ending a session stops its refresh token; an access token already issued in it keeps working
/// until it expires, as after a sign-out.
/// </para>
/// </summary>
public sealed class RevokeSessionUseCase(
    ISessionsService sessions,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<RevokeSessionCommand> validator) : IRevokeSessionUseCase
{
    /// <inheritdoc />
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
