using System.Text.Json.Serialization;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(Authenticated), "authenticated")]
[JsonDerivedType(typeof(TwoFactorRequired), "twoFactorRequired")]
public abstract record LoginOutcome
{
    private LoginOutcome()
    {
    }

    public sealed record Authenticated(
        Guid UserId,
        string Email,
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt) : LoginOutcome;

    public sealed record TwoFactorRequired(string ChallengeToken) : LoginOutcome;
}
