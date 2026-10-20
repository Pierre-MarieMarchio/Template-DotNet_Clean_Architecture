using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.Register;

/// <summary>Registers the address unless it is taken, and mails its holder one message either way.</summary>
public sealed class RegisterUseCase(
    IAccountsService accounts,
    IEmailTokensService emailTokens,
    IAccountEmailFactory emailFactory,
    IEmailSender emailSender,
    ISecurityEventLog securityEventLog,
    IValidator<RegisterCommand> validator,
    ILogger<RegisterUseCase> logger) : IRegisterUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        RegisterCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var registration = await accounts.RegisterAsync(request.Email, request.Password, cancellationToken);

        if (registration.Status is AccountRegistrationStatus.Rejected)
        {
            return Result.Failure(AccountsErrors.RegistrationRejected(
                registration.EmailRejection,
                registration.PasswordRejection));
        }

        if (registration.Status is AccountRegistrationStatus.Created)
        {
            securityEventLog.Record(SecurityEvent.Registered(registration.UserId));
        }

        await TrySendAsync(request.Email, registration, cancellationToken);

        return Result.Success();
    }

    private async Task TrySendAsync(
        string email,
        AccountRegistrationOutcome registration,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = registration is { Status: AccountRegistrationStatus.AlreadyRegistered, EmailConfirmed: true }
                ? await emailFactory.CreateAlreadyRegisteredAsync(email, cancellationToken)
                : await CreateConfirmationAsync(email, cancellationToken);

            if (message is not null)
            {
                await emailSender.SendAsync(email, message.Subject, message.HtmlBody, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to send a registration email.");
        }
    }

    private async Task<AccountEmail?> CreateConfirmationAsync(string email, CancellationToken cancellationToken)
    {
        var token = await emailTokens.IssueEmailConfirmationAsync(email, cancellationToken);

        return token is null ? null : await emailFactory.CreateConfirmationAsync(email, token, cancellationToken);
    }
}
