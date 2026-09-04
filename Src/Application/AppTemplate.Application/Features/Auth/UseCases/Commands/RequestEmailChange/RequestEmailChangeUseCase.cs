using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports.EmailChangeEmailComposer;
using AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RequestEmailChange;

public sealed class RequestEmailChangeUseCase(
    IEmailChangeTokens emailChangeTokens,
    IEmailChangeEmailComposer composer,
    IEmailSender emailSender,
    ICurrentUser currentUser,
    IValidator<RequestEmailChangeCommand> validator,
    ILogger<RequestEmailChangeUseCase> logger) : IRequestEmailChangeUseCase
{
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

        var issued = await emailChangeTokens.IssueAsync(
            userId.Value,
            request.CurrentPassword,
            request.NewEmail,
            cancellationToken);

        if (issued.Status is EmailChangeRequestStatus.IncorrectCurrentPassword)
        {
            return Result.Failure(AuthErrors.IncorrectCurrentPassword);
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
            var message = await composer.ComposeAsync(userName, newEmail, token, cancellationToken);

            await emailSender.SendAsync(newEmail, message.Subject, message.HtmlBody, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The address is not logged, for the reason ResendConfirmationEmailUseCase gives.
            logger.LogError(exception, "Failed to send an email change confirmation.");
        }
    }
}
