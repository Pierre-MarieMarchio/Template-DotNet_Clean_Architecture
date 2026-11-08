using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestEmailChange;

public sealed record RequestEmailChangeCommand(string CurrentPassword, string NewEmail) : ICommand<Result>;

public sealed class RequestEmailChangeCommandHandler(
    IEmailTokensService emailTokens,
    IAccountEmailFactory emailFactory,
    IEmailSender emailSender,
    ICurrentUser currentUser,
    ILogger<RequestEmailChangeCommandHandler> logger) : ICommandHandler<RequestEmailChangeCommand, Result>
{
    public async ValueTask<Result> Handle(
        RequestEmailChangeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId;
        }

        var issued = await emailTokens.IssueEmailChangeAsync(
            userId.Value.Value,
            request.CurrentPassword,
            request.NewEmail,
            cancellationToken);

        if (issued.Status is EmailChangeRequestStatus.NoSuchAccount)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (issued.Status is EmailChangeRequestStatus.IncorrectCurrentPassword)
        {
            return Result.Failure(AccountsErrors.IncorrectCurrentPassword);
        }

        await TrySendAsync(request.NewEmail, issued, cancellationToken);

        return Result.Success();
    }

    private async Task TrySendAsync(string newEmail, EmailChangeRequestOutcome issued, CancellationToken cancellationToken)
    {
        try
        {
            var message = issued.Token is { } token
                ? await emailFactory.CreateEmailChangeAsync(newEmail, token, cancellationToken)
                : await emailFactory.CreateAlreadyRegisteredAsync(newEmail, cancellationToken);

            await emailSender.SendAsync(newEmail, message.Subject, message.HtmlBody, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to send an email change mail.");
        }
    }
}
