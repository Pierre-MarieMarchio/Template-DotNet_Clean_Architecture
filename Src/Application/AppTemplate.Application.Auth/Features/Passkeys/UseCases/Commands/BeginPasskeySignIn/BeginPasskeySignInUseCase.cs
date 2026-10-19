using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeySignIn;

/// <summary>
/// Starts a sign-in with a passkey: the request options a client feeds to
/// <c>navigator.credentials.get()</c> for an authenticator to offer a discoverable credential, and
/// the state that joins the two calls. No account is named and none is disclosed — which account
/// answers is decided by the credential, at the step that follows.
/// </summary>
public sealed class BeginPasskeySignInUseCase(IPasskeyService passkeys) : IBeginPasskeySignInUseCase
{
    /// <inheritdoc />
    public async Task<Result<PasskeySignInChallenge>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        Result.Success(await passkeys.BeginSignInAsync(cancellationToken));
}
