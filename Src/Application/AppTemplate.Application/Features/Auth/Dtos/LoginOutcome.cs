using System.Text.Json.Serialization;

namespace AppTemplate.Application.Features.Auth.Dtos;

/// <summary>
/// What logging in produces: either a token pair, or — once two-factor sign-in exists — a challenge
/// to complete first. Only <see cref="Authenticated"/> is ever produced today; the second branch is
/// added now so that shipping the second factor later does not change the shape every existing
/// client already parses.
/// <para>
/// A closed hierarchy with a JSON type discriminator, rather than one record with nullable
/// branch-specific fields: a caller switches on the concrete type instead of guessing which fields
/// go together, and the wire format carries an explicit "status" tag instead of an implicit shape.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(Authenticated), "authenticated")]
[JsonDerivedType(typeof(TwoFactorRequired), "twoFactorRequired")]
public abstract record LoginOutcome
{
    private LoginOutcome()
    {
    }

    /// <param name="RefreshToken">The raw secret, returned once and never persisted in this form.</param>
    public sealed record Authenticated(
        Guid UserId,
        string UserName,
        string Email,
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt) : LoginOutcome;

    /// <summary>
    /// Not produced by anything today: no use case has a second factor to challenge yet.
    /// </summary>
    /// <param name="ChallengeToken">Identifies the pending sign-in for whichever step completes it.</param>
    public sealed record TwoFactorRequired(string ChallengeToken) : LoginOutcome;
}
