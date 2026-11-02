using AppTemplate.Application.Auth.Features.AccessKeys.Model.Errors;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.RevokeAccessKey;

public sealed class RevokeAccessKeyUseCase(
    IAccessKeysService accessKeys,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<RevokeAccessKeyCommand> validator) : IRevokeAccessKeyUseCase
{
    public async Task<Result> ExecuteAsync(RevokeAccessKeyCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId;
        }

        if (!await accessKeys.RevokeAsync(callerId.Value.Value, request.AccessKeyId, cancellationToken))
        {
            return Result.Failure(AccessKeysErrors.NoSuchAccessKey);
        }

        securityEventLog.Record(SecurityEvent.AccessKeyRevoked(callerId.Value.Value));

        return Result.Success();
    }
}
