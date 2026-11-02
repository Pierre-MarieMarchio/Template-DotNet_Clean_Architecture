using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResetPassword;

public sealed class ResetPasswordUseCase(
    IEmailTokensService emailTokens,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    IValidator<ResetPasswordCommand> validator) : IResetPasswordUseCase
{
    public async Task<Result> ExecuteAsync(ResetPasswordCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var reset = await emailTokens.ResetPasswordAsync(
            request.Email,
            request.Token,
            request.NewPassword,
            cancellationToken);

        if (reset.Status is PasswordResetStatus.Rejected)
        {
            return Result.Failure(
                AccountsErrors.PasswordResetRejected(
                    reset.RejectionMessage ?? "The submitted password does not meet the required policy."));
        }

        if (reset.Status is not PasswordResetStatus.Reset || reset.UserId is not { } userId)
        {
            return Result.Failure(AccountsErrors.InvalidPasswordReset);
        }

        await CredentialInvalidationPolicy.InvalidateAsync(sessionTokens, securityEventLog, userId, cancellationToken);

        return Result.Success();
    }
}
