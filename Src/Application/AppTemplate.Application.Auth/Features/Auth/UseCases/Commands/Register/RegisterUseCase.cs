using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Ports.ConfirmationEmailFactory;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Register;

/// <summary>
/// Creates the account, then tries to mail a confirmation token for it. The account is kept even
/// when the mail cannot be handed over, since failing the call would leave the address taken with
/// no account able to claim it.
/// </summary>
public sealed class RegisterUseCase(
    IUserAccountsService accounts,
    IEmailConfirmationTokensService confirmationTokens,
    IConfirmationEmailFactory emailFactory,
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
            ? AuthErrors.RegistrationConflict
            : AuthErrors.RegistrationRejected(
                creation.RejectionMessage ?? "The submitted password does not meet the required policy.");

    private async Task<bool> TrySendConfirmationEmailAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        try
        {
            // The token is derived from the stored account, so one minted before the row was written
            // could not confirm it.
            var pending = await confirmationTokens.IssueAsync(email, cancellationToken);

            if (pending is null)
            {
                return false;
            }

            var message = await emailFactory.CreateAsync(
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
