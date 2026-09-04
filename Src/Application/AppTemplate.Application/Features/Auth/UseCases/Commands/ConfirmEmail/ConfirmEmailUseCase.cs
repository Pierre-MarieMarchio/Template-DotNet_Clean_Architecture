using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;

/// <summary>
/// Redeeming the token rotates the account's security stamp, which is what makes it single-use.
/// <para>
/// It does <em>not</em> then call <c>CredentialInvalidation</c>, unlike every other operation that
/// rotates a stamp. Sign-in requires a confirmed email, so no session can exist yet and there are no
/// refresh tokens to revoke — calling it would be a no-op dressed as a precaution. A deployment that
/// sets <c>Identity:RequireConfirmedEmail</c> to false changes that: sessions become possible before
/// confirmation, and this use case then needs the same revocation the others make, which in turn
/// needs the redeemed account's id to travel back from the port.
/// </para>
/// </summary>
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
        return outcome is EmailConfirmationStatus.Confirmed
            ? Result.Success()
            : Result.Failure(AuthErrors.InvalidEmailConfirmation);
    }
}
