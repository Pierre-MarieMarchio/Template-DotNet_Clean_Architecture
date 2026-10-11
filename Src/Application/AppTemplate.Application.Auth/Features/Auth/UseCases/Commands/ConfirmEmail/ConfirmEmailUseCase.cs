using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailTokens;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmail;

/// <summary>Redeeming the token rotates the account's security stamp, which is what makes it
/// single-use. It does <em>not</em> then call <c>CredentialInvalidationPolicy</c>, unlike every other
/// stamp-rotating operation: sign-in requires a confirmed email, so no session can exist yet and
/// there are no refresh tokens to revoke. A deployment that sets
/// <c>Identity:RequireConfirmedEmail</c> to false changes that — this use case then needs the same
/// revocation the others make.</summary>
public sealed class ConfirmEmailUseCase(
    IEmailTokensService emailTokens,
    IValidator<ConfirmEmailCommand> validator) : IConfirmEmailUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(ConfirmEmailCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        bool confirmed = await emailTokens.ConfirmEmailAsync(request.Email, request.Token, cancellationToken);

        // One error for every refusal. An unknown address and a wrong token must be indistinguishable,
        // or the endpoint answers "is this address registered?" for anybody holding a junk token.
        return confirmed
            ? Result.Success()
            : Result.Failure(AuthErrors.InvalidEmailConfirmation);
    }
}
