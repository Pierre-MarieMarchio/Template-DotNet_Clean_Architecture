using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResetPassword;

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : ICommand<Result>;

public sealed class ResetPasswordCommandHandler(
    IEmailTokensService emailTokens,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog) : ICommandHandler<ResetPasswordCommand, Result>
{
    public async ValueTask<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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
