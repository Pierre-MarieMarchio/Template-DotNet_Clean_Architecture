using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.Validators;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

/// <summary>
/// Sent in a request body rather than a query string, to keep the single-use token out of access
/// logs, browser history and the <c>Referer</c> header.
/// </summary>
public sealed record ConfirmEmailRequest(string Email, string Token);

public interface IConfirmEmailUseCase : IUseCase<ConfirmEmailRequest, Result>;

public sealed class ConfirmEmailUseCase(
    IEmailConfirmationTokens confirmationTokens,
    IValidator<ConfirmEmailRequest> validator) : IConfirmEmailUseCase
{
    public async Task<Result> ExecuteAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.ValidateAsync(request, cancellationToken);

        if (!validation.IsValid)
        {
            return Result.Failure(validation.ToError());
        }

        var outcome = await confirmationTokens.RedeemAsync(request.Email, request.Token, cancellationToken);

        // One error for every refusal. An unknown address and a wrong token must be indistinguishable,
        // or the endpoint answers "is this address registered?" for anybody holding a junk token.
        return outcome is EmailConfirmationOutcome.Confirmed
            ? Result.Success()
            : Result.Failure(AuthErrors.InvalidEmailConfirmation);
    }
}
