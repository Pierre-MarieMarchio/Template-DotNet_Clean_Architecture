using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Ports;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

public sealed record RequestPasswordResetCommand(string Email);

/// <summary>
/// Without this, an account whose password was forgotten is locked out for good and its address
/// stays taken, the email index being unique. Answers in success for every address — known, unknown
/// or unconfirmed — the same anti-enumeration pattern <c>ResendConfirmationEmailUseCase</c> uses.
/// </summary>
public interface IRequestPasswordResetUseCase : IUseCase<RequestPasswordResetCommand, Result>;

public sealed class RequestPasswordResetUseCase(
    IPasswordResetTokens resetTokens,
    IPasswordResetEmailComposer composer,
    IEmailSender emailSender,
    IValidator<RequestPasswordResetCommand> validator,
    ILogger<RequestPasswordResetUseCase> logger) : IRequestPasswordResetUseCase
{
    public async Task<Result> ExecuteAsync(
        RequestPasswordResetCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var pending = await resetTokens.IssueAsync(request.Email, cancellationToken);

        if (pending is not null)
        {
            await TrySendAsync(request.Email, pending, cancellationToken);
        }

        // Always success. The answer must not differ between "sent" and "no such account".
        return Result.Success();
    }

    private async Task TrySendAsync(
        string email,
        PendingPasswordReset pending,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await composer.ComposeAsync(
                pending.UserName,
                email,
                pending.Token,
                cancellationToken);

            await emailSender.SendAsync(email, message.Subject, message.HtmlBody, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The address is not logged, for the reason ResendConfirmationEmailUseCase gives.
            logger.LogError(exception, "Failed to send a password reset email.");
        }
    }
}
