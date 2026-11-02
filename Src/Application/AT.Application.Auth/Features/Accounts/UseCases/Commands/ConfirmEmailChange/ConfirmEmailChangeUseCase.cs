using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeUseCase(
    IEmailTokensService emailTokens,
    ISessionTokensService sessionTokens,
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

        var confirmation = await emailTokens.ChangeEmailAsync(
            userId.Value.Value,
            request.NewEmail,
            request.Token,
            cancellationToken);

        if (confirmation.Status is EmailChangeStatus.Rejected)
        {
            return Result.Failure(
                AccountsErrors.EmailChangeRejected(
                    confirmation.RejectionMessage ?? "The new email address was rejected."));
        }

        if (confirmation.Status is not EmailChangeStatus.Changed)
        {
            return Result.Failure(AccountsErrors.InvalidEmailChange);
        }

        await CredentialInvalidationPolicy.InvalidateAsync(sessionTokens, securityEventLog, userId.Value.Value, cancellationToken);

        return Result.Success();
    }
}
