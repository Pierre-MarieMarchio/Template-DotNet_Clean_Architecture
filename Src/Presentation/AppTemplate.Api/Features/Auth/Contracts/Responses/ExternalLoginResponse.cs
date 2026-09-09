using System.Text.Json.Serialization;

namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// What signing in through an identity provider produces on the wire: the same two branches, the same
/// <c>status</c> tag and the same names as <see cref="LoginResponse"/>, so a client that already
/// parses a password sign-in parses this one unchanged — including the branch that is not a sign-in
/// yet.
/// </summary>
/// <remarks>
/// <see cref="Authenticated.AccountCreated"/> is meaningful only here: a password sign-in cannot
/// bring an account into existence.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(Authenticated), "authenticated")]
[JsonDerivedType(typeof(TwoFactorRequired), "twoFactorRequired")]
public abstract record ExternalLoginResponse
{
    private ExternalLoginResponse()
    {
    }

    /// <param name="Tokens">Nested for the reason <see cref="LoginResponse.Authenticated"/> gives.</param>
    /// <param name="AccountCreated">
    /// Whether this call is what created the account, so a client can send a first-run experience
    /// rather than infer one from an empty profile.
    /// </param>
    public sealed record Authenticated(TokenResponse Tokens, bool AccountCreated) : ExternalLoginResponse;

    /// <summary>
    /// The account has a second factor armed, and the provider does not stand in for it. Redeemed at
    /// <c>POST /auth/login/two-factor</c>, exactly as a password sign-in's challenge is.
    /// </summary>
    /// <param name="ChallengeToken">Identifies the pending sign-in.</param>
    public sealed record TwoFactorRequired(string ChallengeToken) : ExternalLoginResponse;
}
