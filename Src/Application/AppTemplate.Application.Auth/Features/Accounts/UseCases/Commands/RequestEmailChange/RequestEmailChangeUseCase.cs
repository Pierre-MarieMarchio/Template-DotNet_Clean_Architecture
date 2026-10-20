using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestEmailChange;

/// <summary>
/// Once the password is verified, mails the new address a confirmation link, or the notice that an
/// account already holds it, and succeeds the same way for both.
/// </summary>
public sealed class RequestEmailChangeUseCase(
    IEmailTokensService emailTokens,
    IAccountEmailFactory emailFactory,
    IEmailSender emailSender,
    ICurrentUser currentUser,
    IValidator<RequestEmailChangeCommand> validator,
    ILogger<RequestEmailChangeUseCase> logger) : IRequestEmailChangeUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        RequestEmailChangeCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

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

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it.
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
