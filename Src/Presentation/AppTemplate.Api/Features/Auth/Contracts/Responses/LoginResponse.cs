using System.Text.Json.Serialization;

namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// What signing in produces on the wire: either a token pair, or — once two-factor sign-in exists —
/// a challenge to complete first. The shape carries an explicit <c>status</c> tag, so a client reads
/// which outcome it got instead of inferring it from which fields happen to be present.
/// <para>
/// No profile fields: a caller that wants the account it just signed in as reads
/// <c>GET /auth/me</c>, which is the single definition of a profile.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(Authenticated), "authenticated")]
[JsonDerivedType(typeof(TwoFactorRequired), "twoFactorRequired")]
public abstract record LoginResponse
{
    private LoginResponse()
    {
    }

    /// <summary>
    /// The pair is nested under <c>tokens</c> rather than spread across this branch so that
    /// <see cref="TokenResponse"/> stays a single definition, shared with <c>POST /auth/refresh</c>.
    /// </summary>
    public sealed record Authenticated(TokenResponse Tokens) : LoginResponse;

    /// <summary>Not produced today: no use case has a second factor to challenge yet.</summary>
    /// <param name="ChallengeToken">Identifies the pending sign-in for whichever step completes it.</param>
    public sealed record TwoFactorRequired(string ChallengeToken) : LoginResponse;
}
