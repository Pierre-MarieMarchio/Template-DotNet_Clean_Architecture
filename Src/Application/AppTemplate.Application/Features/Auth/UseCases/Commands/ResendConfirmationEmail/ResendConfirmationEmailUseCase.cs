using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Ports.ConfirmationEmailFactory;
using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ResendConfirmationEmail;

public sealed class ResendConfirmationEmailUseCase(
    IEmailConfirmationTokensService confirmationTokens,
    IConfirmationEmailFactory emailFactory,
    IEmailSender emailSender,
    IValidator<ResendConfirmationEmailCommand> validator,
    ILogger<ResendConfirmationEmailUseCase> logger) : IResendConfirmationEmailUseCase
{
    public async Task<Result> ExecuteAsync(
        ResendConfirmationEmailCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var pending = await confirmationTokens.IssueAsync(request.Email, cancellationToken);

        if (pending is not null)
        {
            await TrySendAsync(request.Email, pending, cancellationToken);
        }

        // Always success. The answer must not differ between "sent", "already confirmed" and "no
        // such account", including when delivery itself failed.
        return Result.Success();
    }

    private async Task TrySendAsync(
        string email,
        PendingConfirmation pending,
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
            // The address is not logged: this endpoint answers identically for every address, and a
            // log entry naming the ones that reached the relay would undo that.
            logger.LogError(exception, "Failed to resend a confirmation email.");
        }
    }
}
