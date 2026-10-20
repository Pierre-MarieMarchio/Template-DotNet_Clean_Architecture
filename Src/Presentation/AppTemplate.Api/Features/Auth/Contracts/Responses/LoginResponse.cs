using System.Text.Json.Serialization;

namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// A token pair, or a challenge to complete with <c>POST /auth/login/two-factor</c> when two-factor
/// sign-in is armed.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(Authenticated), "authenticated")]
[JsonDerivedType(typeof(TwoFactorRequired), "twoFactorRequired")]
public abstract record LoginResponse
{
    private LoginResponse()
    {
    }

    /// <summary>A completed sign-in, its token pair under <c>tokens</c>.</summary>
    public sealed record Authenticated(TokenResponse Tokens) : LoginResponse;

    /// <param name="ChallengeToken">
    /// Identifies the pending sign-in for <c>POST /auth/login/two-factor</c>.
    /// </param>
    public sealed record TwoFactorRequired(string ChallengeToken) : LoginResponse;
}
