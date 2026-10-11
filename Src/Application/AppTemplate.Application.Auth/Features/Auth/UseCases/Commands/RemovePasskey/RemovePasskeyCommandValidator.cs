using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RemovePasskey;

/// <summary>
/// An empty id is refused here so it never reaches the store as a lookup that misses. Nothing else
/// is inspected: a WebAuthn credential id is opaque bytes, and any shape constraint here would
/// refuse a legitimate credential for the sake of a rule no authenticator agreed to.
/// </summary>
public sealed class RemovePasskeyCommandValidator : AbstractValidator<RemovePasskeyCommand>
{
    /// <summary>The credential id is required.</summary>
    public RemovePasskeyCommandValidator() => RuleFor(x => x.CredentialId).NotEmpty();
}
