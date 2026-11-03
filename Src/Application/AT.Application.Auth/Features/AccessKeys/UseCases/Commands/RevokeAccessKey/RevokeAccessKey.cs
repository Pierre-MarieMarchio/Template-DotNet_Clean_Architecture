using AppTemplate.Application.Auth.Features.AccessKeys.Model.Errors;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.RevokeAccessKey;

public sealed record RevokeAccessKeyCommand(Guid AccessKeyId) : ICommand<Result>;

public sealed class RevokeAccessKeyCommandHandler(
    IAccessKeysService accessKeys,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser) : ICommandHandler<RevokeAccessKeyCommand, Result>
{
    public async ValueTask<Result> Handle(RevokeAccessKeyCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId;
        }

        if (!await accessKeys.RevokeAsync(callerId.Value.Value, request.AccessKeyId, cancellationToken))
        {
            return Result.Failure(AccessKeysErrors.NoSuchAccessKey);
        }

        await securityEventLog.RecordAsync(SecurityEvent.AccessKeyRevoked(callerId.Value.Value));

        return Result.Success();
    }
}
