using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;

public sealed class ConfirmEmailUseCase(
    IEmailConfirmationTokens confirmationTokens,
    IValidator<ConfirmEmailCommand> validator) : IConfirmEmailUseCase
{
    public async Task<Result> ExecuteAsync(ConfirmEmailCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var outcome = await confirmationTokens.RedeemAsync(request.Email, request.Token, cancellationToken);

        // One error for every refusal. An unknown address and a wrong token must be indistinguishable,
        // or the endpoint answers "is this address registered?" for anybody holding a junk token.
        return outcome is EmailConfirmationOutcome.Confirmed
            ? Result.Success()
            : Result.Failure(AuthErrors.InvalidEmailConfirmation);
    }
}
