using System.Security.Cryptography;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;
using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Directories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.ExternalLogins.Adapters.Services;

public sealed class ExternalIdentityVerifierTests : IDisposable
{
    private const string _providerName = "google";
    private const string _issuer = "https://accounts.google.com";
    private const string _audience = "1234.apps.googleusercontent.com";
    private const string _subject = "108412345678901234567";
    private const string _currentKeyId = "current";
    private const string _rotatedKeyId = "rotated";

    private readonly RSA _currentKey = RSA.Create(2048);
    private readonly RSA _rotatedKey = RSA.Create(2048);

    private readonly ISigningKeyDirectory _signingKeys = Substitute.For<ISigningKeyDirectory>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _currentKey.Dispose();
        _rotatedKey.Dispose();
    }

    [Fact]
    public async Task VerifyAsync_AcceptsATokenTheProviderSigned()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token());

        identity.ShouldNotBeNull();
        identity.Provider.ShouldBe(_providerName);
        identity.Subject.ShouldBe(_subject);
        identity.Email.ShouldBe("someone@example.com");
        identity.EmailVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyAsync_ReportsTheConfiguredProviderName_NotTheOneTheClientTyped()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(), presentedProvider: "GOOGLE");

        identity.ShouldNotBeNull();
        identity.Provider.ShouldBe(_providerName);
    }

    [Fact]
    public async Task VerifyAsync_RefusesAProviderNobodyConfigured()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(), presentedProvider: "okta");

        identity.ShouldBeNull();
        await _signingKeys.DidNotReceive().GetAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenSignedByAKeyTheProviderNeverPublished()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(signedWith: _rotatedKey, keyId: _currentKeyId));

        identity.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenMintedForAnotherAudience()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(audience: "9999.apps.googleusercontent.com"));

        identity.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenFromAnotherIssuer()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(issuer: "https://login.microsoftonline.com/tenant/v2.0"));

        identity.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenThatHasExpired()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1)));

        identity.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenThatIsNotYetValid()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(
            notBefore: DateTime.UtcNow.AddHours(1),
            expires: DateTime.UtcNow.AddHours(2)));

        identity.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenSignedWithAnAlgorithmThisAdapterDoesNotAccept()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(algorithm: SecurityAlgorithms.RsaSha512));

        identity.ShouldBeRefusedAsInvalid();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task VerifyAsync_RefusesSomethingThatIsNotAToken(string idToken)
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        (await Verify(idToken)).ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesAnAuthenticTokenThatNamesNoSubject()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        (await Verify(Token(subject: null))).ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_ReadsEmailVerifiedWhenTheProviderEncodesItAsAString()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(emailVerified: "true"));

        identity.ShouldNotBeNull();
        identity.EmailVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyAsync_TreatsAnAbsentEmailVerifiedAsFalse()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(withEmailVerified: false));

        identity.ShouldNotBeNull();
        identity.EmailVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyAsync_AcceptsATokenCarryingNoAddress()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(email: null));

        identity.ShouldNotBeNull();
        identity.Email.ShouldBeNull();
    }

    [Fact]
    public async Task VerifyAsync_RefetchesTheKeySetWhenTheTokenNamesAKeyTheCacheDoesNotHold()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));
        _signingKeys.RefreshAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>())
            .Returns([PublicKey(_rotatedKey, _rotatedKeyId)]);

        var identity = await Verify(Token(signedWith: _rotatedKey, keyId: _rotatedKeyId));

        identity.ShouldNotBeNull();
        await _signingKeys.Received(1)
            .RefreshAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_DoesNotRefetchWhenTheNamedKeyIsHeldAndTheSignatureIsWrong()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var identity = await Verify(Token(signedWith: _rotatedKey, keyId: _currentKeyId));

        identity.ShouldBeRefusedAsInvalid();
        await _signingKeys.DidNotReceive()
            .RefreshAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_RefusesEveryTokenWhenNoKeyIsAvailable()
    {
        GivenTheProviderPublishes();

        (await Verify(Token())).ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_ObservesCancellationOnEntry()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateVerifier().VerifyAsync(_providerName, Token(), cancelled.Token));
    }

    private void GivenTheProviderPublishes(params SecurityKey[] keys) =>
        _signingKeys.GetAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>())
            .Returns(keys);

    private Task<VerifiedExternalIdentity?> Verify(string idToken, string presentedProvider = _providerName) =>
        CreateVerifier().VerifyAsync(presentedProvider, idToken, TestToken);

    private ExternalIdentityVerifier CreateVerifier()
    {
        var provider = new ExternalIdentityProviderOptions
        {
            Name = _providerName,
            JwksUri = "https://www.googleapis.com/oauth2/v3/certs",
        };

        provider.Issuers.Add(_issuer);
        provider.Audiences.Add(_audience);

        var options = new ExternalIdentityOptions();
        options.Providers.Add(provider);

        return new ExternalIdentityVerifier(new OptionsWrapper<ExternalIdentityOptions>(options), _signingKeys);
    }

    private static RsaSecurityKey PublicKey(RSA key, string keyId) =>
        new(key.ExportParameters(includePrivateParameters: false)) { KeyId = keyId };

    private string Token(
        RSA? signedWith = null,
        string keyId = _currentKeyId,
        string issuer = _issuer,
        string audience = _audience,
        string? subject = _subject,
        string? email = "someone@example.com",
        object? emailVerified = null,
        bool withEmailVerified = true,
        string algorithm = SecurityAlgorithms.RsaSha256,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);

        if (subject is not null)
        {
            claims[JwtRegisteredClaimNames.Sub] = subject;
        }

        if (email is not null)
        {
            claims[JwtRegisteredClaimNames.Email] = email;
        }

        if (withEmailVerified)
        {
            claims[JwtRegisteredClaimNames.EmailVerified] = emailVerified ?? true;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(signedWith ?? _currentKey) { KeyId = keyId },
                algorithm),
        });
    }
}

internal static class ExternalIdentityRefusalAssertions
{
    internal static void ShouldBeRefusedAsInvalid(this VerifiedExternalIdentity? identity) =>
        identity.ShouldBeNull();
}
