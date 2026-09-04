using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Dtos;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.Validators;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

public sealed record RegisterRequest(string UserName, string Email, string Password);

public interface IRegisterUseCase : IUseCase<RegisterRequest, Result<RegisterResponse>>;

public sealed class RegisterUseCase(
    IUserAccounts accounts,
    IEmailConfirmationTokens confirmationTokens,
    IConfirmationEmailComposer composer,
    IEmailSender emailSender,
    IValidator<RegisterRequest> validator,
    ILogger<RegisterUseCase> logger) : IRegisterUseCase
{
    public async Task<Result<RegisterResponse>> ExecuteAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken);

        if (!validation.IsValid)
        {
            return Result.Failure<RegisterResponse>(validation.ToError());
        }

        var creation = await accounts.CreateAsync(
            request.UserName,
            request.Email,
            request.Password,
            cancellationToken);

        if (creation.Outcome is not AccountCreationOutcome.Created)
        {
            return Result.Failure<RegisterResponse>(ToError(creation));
        }

        // The account is committed before anything is delivered. Letting an unreachable relay fail
        // the call would leave an unconfirmable account behind with its address taken and no way to
        // ask for another link, so the outcome travels as a flag and the resend endpoint is the
        // recovery path.
        bool confirmationEmailSent = await TrySendConfirmationEmailAsync(
            creation.UserId,
            request.Email,
            cancellationToken);

        return Result.Success(new RegisterResponse(
            creation.UserId,
            request.UserName,
            request.Email,
            confirmationEmailSent));
    }

    private static Error ToError(AccountCreation creation) =>
        creation.Outcome is AccountCreationOutcome.Conflict
            ? AuthErrors.RegistrationConflict
            : AuthErrors.RegistrationRejected(creation.RejectionMessage ?? string.Empty);

    private async Task<bool> TrySendConfirmationEmailAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        try
        {
            // Issued after the account exists, because the token is derived from the stored account:
            // one minted before the row was written could not confirm it.
            var pending = await confirmationTokens.IssueAsync(email, cancellationToken);

            if (pending is null)
            {
                return false;
            }

            var message = await composer.ComposeAsync(
                pending.UserName,
                email,
                pending.Token,
                cancellationToken);

            await emailSender.SendAsync(email, message.Subject, message.HtmlBody, cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Any transport or template failure. The caller is told delivery did not happen; the
            // exception itself never reaches the client.
            logger.LogError(exception, "Failed to send the confirmation email for user {UserId}.", userId);

            return false;
        }
    }
}
