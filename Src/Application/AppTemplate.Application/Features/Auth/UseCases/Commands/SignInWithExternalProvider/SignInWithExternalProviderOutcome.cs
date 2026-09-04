using System.Text.Json.Serialization;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

/// <summary>
/// What signing in through an identity provider produces. The same two shapes <c>LoginUseCase</c>
/// produces, and deliberately so: a provider proves who the caller is, which is exactly what a
/// password proves, and neither is a reason to skip a second factor the account owner armed.
/// <para>
/// A closed hierarchy with a JSON type discriminator, for the reason <c>LoginOutcome</c> gives.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(Authenticated), "authenticated")]
[JsonDerivedType(typeof(TwoFactorRequired), "twoFactorRequired")]
public abstract record SignInWithExternalProviderOutcome
{
    private SignInWithExternalProviderOutcome()
    {
    }

    /// <param name="RefreshToken">The raw secret, returned once and never persisted in this form.</param>
    /// <param name="AccountCreated">
    /// Whether this sign-in is what brought the account into existence. A client uses it to send a
    /// first-run experience instead of guessing from an empty profile.
    /// </param>
    public sealed record Authenticated(
        Guid UserId,
        string UserName,
        string Email,
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt,
        bool AccountCreated) : SignInWithExternalProviderOutcome;

    /// <param name="ChallengeToken">Identifies the pending sign-in for <c>VerifyTwoFactorUseCase</c>.</param>
    public sealed record TwoFactorRequired(string ChallengeToken) : SignInWithExternalProviderOutcome;
}
