using AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetEmailFactory;
using AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RequestPasswordReset;

/// <summary>
/// Mints a reset token where there is an account to mint one for, and mails it. Succeeds either
/// way, and a delivery failure is logged without the address it was bound for.
/// </summary>
public sealed class RequestPasswordResetUseCase(
    IPasswordResetTokensService resetTokens,
    IPasswordResetEmailFactory emailFactory,
    IEmailSender emailSender,
    IValidator<RequestPasswordResetCommand> validator,
    ILogger<RequestPasswordResetUseCase> logger) : IRequestPasswordResetUseCase
{
    /// <inheritdoc />
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
            var message = await emailFactory.CreateAsync(
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
