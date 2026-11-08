using System.Text.Json.Serialization;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(Authenticated), "authenticated")]
[JsonDerivedType(typeof(TwoFactorRequired), "twoFactorRequired")]
public abstract record SignInWithExternalProviderOutcome
{
    private SignInWithExternalProviderOutcome()
    {
    }

    public sealed record Authenticated(
        Guid UserId,
        string Email,
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt,
        bool AccountCreated) : SignInWithExternalProviderOutcome;

    public sealed record TwoFactorRequired(string ChallengeToken) : SignInWithExternalProviderOutcome;
}
