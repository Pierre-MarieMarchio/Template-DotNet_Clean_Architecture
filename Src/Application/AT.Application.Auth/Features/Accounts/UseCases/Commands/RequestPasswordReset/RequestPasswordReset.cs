using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestPasswordReset;

public sealed record RequestPasswordResetCommand(string Email) : ICommand<Result>;

public sealed class RequestPasswordResetCommandHandler(
    IEmailTokensService emailTokens,
    IAccountEmailFactory emailFactory,
    IEmailSender emailSender,
    ILogger<RequestPasswordResetCommandHandler> logger) : ICommandHandler<RequestPasswordResetCommand, Result>
{
    public async ValueTask<Result> Handle(
        RequestPasswordResetCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? token = await emailTokens.IssuePasswordResetAsync(request.Email, cancellationToken);

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
            var message = await emailFactory.CreatePasswordResetAsync(email, token, cancellationToken);

            await emailSender.SendAsync(email, message.Subject, message.HtmlBody, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to send a password reset email.");
        }
    }
}
