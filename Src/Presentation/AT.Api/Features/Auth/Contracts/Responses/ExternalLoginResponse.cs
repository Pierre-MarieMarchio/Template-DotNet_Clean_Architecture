using System.Text.Json.Serialization;

namespace AppTemplate.Api.Features.Auth.Contracts.Responses;

/// <summary>
/// A provider sign-in's answer, with the branches, <c>status</c> tag and names of <see
/// cref="LoginResponse"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(Authenticated), "authenticated")]
[JsonDerivedType(typeof(TwoFactorRequired), "twoFactorRequired")]
public abstract record ExternalLoginResponse
{
    private ExternalLoginResponse()
    {
    }

    /// <param name="Tokens">The token pair, nested as in <see cref="LoginResponse.Authenticated"/>.</param>
    /// <param name="AccountCreated">Whether this call is what created the account.</param>
    public sealed record Authenticated(TokenResponse Tokens, bool AccountCreated) : ExternalLoginResponse;

    /// <summary>The account has a second factor armed, and the provider does not stand in for it.</summary>
    /// <param name="ChallengeToken">Identifies the pending sign-in.</param>
    public sealed record TwoFactorRequired(string ChallengeToken) : ExternalLoginResponse;
}
