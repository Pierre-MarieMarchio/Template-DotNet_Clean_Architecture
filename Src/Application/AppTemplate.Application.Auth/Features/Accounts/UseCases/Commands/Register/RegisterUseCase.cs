using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.Register;

/// <summary>
/// Creates the account, then tries to mail a confirmation token for it. The account is kept even
/// when the mail cannot be handed over, since failing the call would leave the address taken with
/// no account able to claim it.
/// </summary>
public sealed class RegisterUseCase(
    IAccountsService accounts,
    IEmailTokensService emailTokens,
    IAccountEmailFactory emailFactory,
    IEmailSender emailSender,
    ISecurityEventLog securityEventLog,
    IValidator<RegisterCommand> validator,
    ILogger<RegisterUseCase> logger) : IRegisterUseCase
{
    /// <inheritdoc />
    public async Task<Result<RegisterOutcome>> ExecuteAsync(
        RegisterCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<RegisterOutcome>();
        }

        var creation = await accounts.CreateAsync(
            request.UserName,
            request.Email,
            request.Password,
            cancellationToken);

        if (creation.Status is not AccountCreationStatus.Created)
        {
            return Result.Failure<RegisterOutcome>(ToError(creation));
        }

        securityEventLog.Record(SecurityEvent.Registered(creation.UserId));

        // The account is committed before delivery: an unreachable relay failing the call would
        // leave an account whose address is taken and which nothing can confirm. The outcome travels
        // as a flag, and the resend endpoint is the recovery path.
        bool confirmationEmailSent = await TrySendConfirmationEmailAsync(
            creation.UserId,
            request.Email,
            cancellationToken);

        return Result.Success(new RegisterOutcome(
            creation.UserId,
            request.UserName,
            request.Email,
            confirmationEmailSent));
    }

    private static Error ToError(AccountCreationOutcome creation) =>
        creation.Status is AccountCreationStatus.Conflict
            ? AccountsErrors.RegistrationConflict
            : AccountsErrors.RegistrationRejected(
                creation.UserNameRejection,
                creation.EmailRejection,
                creation.PasswordRejection);

    private async Task<bool> TrySendConfirmationEmailAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        try
        {
            // The token is derived from the stored account, so one minted before the row was written
            // could not confirm it.
            var pending = await emailTokens.IssueEmailConfirmationAsync(email, cancellationToken);

            if (pending is null)
            {
                return false;
            }

            var message = await emailFactory.CreateConfirmationAsync(
                pending.UserName,
                email,
                pending.Token,
                cancellationToken);

            await emailSender.SendAsync(email, message.Subject, message.HtmlBody, cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Any transport or template failure. The caller is told delivery did not happen, and the
            // exception reaches the log only.
            logger.LogError(exception, "Failed to send the confirmation email for user {UserId}.", userId);

            return false;
        }
    }
}
