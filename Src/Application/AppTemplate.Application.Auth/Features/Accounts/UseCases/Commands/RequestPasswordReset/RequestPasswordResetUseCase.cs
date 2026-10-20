using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetUseCase(
    IEmailTokensService emailTokens,
    IAccountEmailFactory emailFactory,
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

        var token = await emailTokens.IssuePasswordResetAsync(request.Email, cancellationToken);

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
