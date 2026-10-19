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
/// Mints a token for the new address and mails it. Succeeds once the password is verified whether
/// or not anything was sent, so the response cannot be read as an answer to "is that address
/// already registered?".
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

        if (issued.Token is { } token)
        {
            await TrySendAsync(request.NewEmail, issued.UserName ?? string.Empty, token, cancellationToken);
        }

        // Always success once the password is verified: the answer must not differ between "sent"
        // and "that address is already registered", or the endpoint becomes a way to test which
        // addresses exist.
        return Result.Success();
    }

    private async Task TrySendAsync(string newEmail, string userName, string token, CancellationToken cancellationToken)
    {
        try
        {
            var message = await emailFactory.CreateEmailChangeAsync(userName, newEmail, token, cancellationToken);

            await emailSender.SendAsync(newEmail, message.Subject, message.HtmlBody, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The address is not logged, for the reason ResendConfirmationEmailUseCase gives.
            logger.LogError(exception, "Failed to send an email change confirmation.");
        }
    }
}
