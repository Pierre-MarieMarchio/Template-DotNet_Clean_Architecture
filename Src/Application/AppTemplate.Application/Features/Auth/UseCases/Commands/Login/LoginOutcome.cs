using System.Text.Json.Serialization;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.Login;

/// <summary>
/// What logging in produces: either a token pair, or — for an account with two-factor sign-in armed
/// — a challenge to complete first with <c>VerifyTwoFactorUseCase</c>.
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

    /// <param name="ChallengeToken">Identifies the pending sign-in for <c>VerifyTwoFactorUseCase</c>.</param>
    public sealed record TwoFactorRequired(string ChallengeToken) : LoginOutcome;
}
