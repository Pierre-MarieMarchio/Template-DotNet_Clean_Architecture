using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResendConfirmationEmail;

public sealed record ResendConfirmationEmailCommand(string Email) : ICommand<Result>;

public sealed class ResendConfirmationEmailCommandHandler(
    IEmailTokensService emailTokens,
    IAccountEmailFactory emailFactory,
    IEmailSender emailSender,
    ILogger<ResendConfirmationEmailCommandHandler> logger) : ICommandHandler<ResendConfirmationEmailCommand, Result>
{
    public async ValueTask<Result> Handle(
        ResendConfirmationEmailCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? token = await emailTokens.IssueEmailConfirmationAsync(request.Email, cancellationToken);

        if (token is not null)
        {
            await TrySendAsync(request.Email, token, cancellationToken);
        }

        return Result.Success();
    }

    private async Task TrySendAsync(
        string email,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await emailFactory.CreateConfirmationAsync(email, token, cancellationToken);

            await emailSender.SendAsync(email, message.Subject, message.HtmlBody, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to resend a confirmation email.");
        }
    }
}
