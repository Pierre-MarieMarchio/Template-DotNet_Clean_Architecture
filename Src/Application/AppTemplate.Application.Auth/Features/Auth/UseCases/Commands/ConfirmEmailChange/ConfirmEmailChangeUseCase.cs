using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailChangeTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmailChange;

/// <summary>
/// Applies the change, then invalidates every credential issued against the old address through
/// <c>CredentialInvalidationPolicy</c>: the store's own stamp rotation fails the access tokens, and
/// the refresh tokens it leaves alone are this call's to revoke.
/// </summary>
public sealed class ConfirmEmailChangeUseCase(
    IEmailChangeTokensService emailChangeTokens,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<ConfirmEmailChangeCommand> validator) : IConfirmEmailChangeUseCase
{
    /// <inheritdoc />
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

        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, userId.Value, cancellationToken);

        return Result.Success();
    }
}
