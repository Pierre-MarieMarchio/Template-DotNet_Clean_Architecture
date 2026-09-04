using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeUseCase(
    IEmailChangeTokens emailChangeTokens,
    IRefreshTokenGrants refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<ConfirmEmailChangeCommand> validator) : IConfirmEmailChangeUseCase
{
    public async Task<Result> ExecuteAsync(
        ConfirmEmailChangeCommand request,
        CancellationToken cancellationToken = default)
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

        var confirmation = await emailChangeTokens.RedeemAsync(
            userId.Value,
            request.NewEmail,
            request.Token,
            cancellationToken);

        if (confirmation.Status is EmailChangeConfirmationStatus.Rejected)
        {
            return Result.Failure(
                AuthErrors.EmailChangeRejected(
                    confirmation.RejectionMessage ?? "The new email address was rejected."));
        }

        if (confirmation.Status is not EmailChangeConfirmationStatus.Changed)
        {
            return Result.Failure(AuthErrors.InvalidEmailChange);
        }

        await CredentialInvalidation.InvalidateAsync(refreshTokens, securityEventLog, userId.Value, cancellationToken);

        return Result.Success();
    }
}
