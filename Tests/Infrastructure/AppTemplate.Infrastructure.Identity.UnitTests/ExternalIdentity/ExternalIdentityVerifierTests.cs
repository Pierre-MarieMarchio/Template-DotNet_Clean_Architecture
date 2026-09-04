using System.Security.Cryptography;
using AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Infrastructure.Identity.ExternalIdentity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.ExternalIdentity;

/// <summary>
/// The four checks that stand between an <c>id_token</c> a client posted and an account: the
/// signature, the issuer, the audience and the validity window. Each is broken on its own here,
/// because a suite that only ever presents a good token proves that the happy path works and nothing
/// whatsoever about what is refused.
/// <para>
/// The tokens are real: this file holds its own signing keys and mints tokens with the same handler
/// the adapter validates them with, so what is under test is the adapter's parameters rather than a
/// substitute's opinion of them.
/// </para>
/// <para>
/// Nothing here moves a clock. Neither <c>JsonWebTokenHandler</c> nor anything under it reads this
/// repository's injectable clock, so expiry is expressed as a real offset from the wall clock, far
/// enough outside the thirty-second tolerance to be unambiguous.
/// </para>
/// </summary>
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

        var outcome = await Verify(Token());

        outcome.Status.ShouldBe(ExternalIdentityStatus.Verified);
        outcome.Identity.ShouldNotBeNull();
        outcome.Identity.Provider.ShouldBe(_providerName);
        outcome.Identity.Subject.ShouldBe(_subject);
        outcome.Identity.Email.ShouldBe("someone@example.com");
        outcome.Identity.EmailVerified.ShouldBeTrue();
    }

    /// <summary>
    /// The name the identity carries is the operator's, not the client's, so a downstream lookup
    /// keyed on it cannot be split in two by a client that varies its capitalisation.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ReportsTheConfiguredProviderName_NotTheOneTheClientTyped()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(), presentedProvider: "GOOGLE");

        outcome.Status.ShouldBe(ExternalIdentityStatus.Verified);
        outcome.Identity!.Provider.ShouldBe(_providerName);
    }

    /// <summary>
    /// A name nobody configured never reaches a key set at all — which is also what keeps this
    /// endpoint from being used to enumerate which providers an installation accepts.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_RefusesAProviderNobodyConfigured()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(), presentedProvider: "okta");

        outcome.Status.ShouldBe(ExternalIdentityStatus.UnknownProvider);
        outcome.Identity.ShouldBeNull();
        await _signingKeys.DidNotReceive().GetAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenSignedByAKeyTheProviderNeverPublished()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        // Same key identifier, different key: the set is consulted, the key is found, and the
        // signature does not hold.
        var outcome = await Verify(Token(signedWith: _rotatedKey, keyId: _currentKeyId));

        outcome.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenMintedForAnotherAudience()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(audience: "9999.apps.googleusercontent.com"));

        outcome.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenFromAnotherIssuer()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(issuer: "https://login.microsoftonline.com/tenant/v2.0"));

        outcome.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenThatHasExpired()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1)));

        outcome.ShouldBeRefusedAsInvalid();
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenThatIsNotYetValid()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(
            notBefore: DateTime.UtcNow.AddHours(1),
            expires: DateTime.UtcNow.AddHours(2)));

        outcome.ShouldBeRefusedAsInvalid();
    }

    /// <summary>
    /// Every provider signs with RS256; nothing here accepts anything else, so a token that
    /// nominates its own algorithm is refused even when the key it names is one the provider really
    /// published.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_RefusesATokenSignedWithAnAlgorithmThisAdapterDoesNotAccept()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(algorithm: SecurityAlgorithms.RsaSha512));

        outcome.ShouldBeRefusedAsInvalid();
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

    /// <summary>
    /// A token with no <c>sub</c> can be perfectly authentic and still identify nobody, and the pair
    /// (provider, subject) is the only key a local account is ever resolved by.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_RefusesAnAuthenticTokenThatNamesNoSubject()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        (await Verify(Token(subject: null))).ShouldBeRefusedAsInvalid();
    }

    /// <summary>
    /// Apple encodes <c>email_verified</c> as the string <c>"true"</c> where Google sends the JSON
    /// boolean. Reading only the boolean would report every Apple address as unverified, and the use
    /// case above refuses a first link on exactly that flag — so every first Apple sign-in would
    /// fail, in production only, with an error saying nothing about why.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ReadsEmailVerifiedWhenTheProviderEncodesItAsAString()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(emailVerified: "true"));

        outcome.Status.ShouldBe(ExternalIdentityStatus.Verified);
        outcome.Identity!.EmailVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyAsync_TreatsAnAbsentEmailVerifiedAsFalse()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(withEmailVerified: false));

        outcome.Status.ShouldBe(ExternalIdentityStatus.Verified);
        outcome.Identity!.EmailVerified.ShouldBeFalse();
    }

    /// <summary>
    /// Apple sends an address on the first authorisation only, so a token without one is the normal
    /// case rather than a fault.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_AcceptsATokenCarryingNoAddress()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(email: null));

        outcome.Status.ShouldBe(ExternalIdentityStatus.Verified);
        outcome.Identity!.Email.ShouldBeNull();
    }

    /// <summary>
    /// The rotation case. A cached key set that predates a rotation names none of the keys in
    /// circulation, and a cache lifetime alone would refuse every sign-in until it lapsed.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_RefetchesTheKeySetWhenTheTokenNamesAKeyTheCacheDoesNotHold()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));
        _signingKeys.RefreshAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>())
            .Returns([PublicKey(_rotatedKey, _rotatedKeyId)]);

        var outcome = await Verify(Token(signedWith: _rotatedKey, keyId: _rotatedKeyId));

        outcome.Status.ShouldBe(ExternalIdentityStatus.Verified);
        await _signingKeys.Received(1)
            .RefreshAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The other half of the same decision, and the reason it is not simply "re-fetch on any
    /// failure": a signature that does not hold against a key the provider really published is not a
    /// rotation, and re-fetching would hand anyone with a forged token an outbound request each.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_DoesNotRefetchWhenTheNamedKeyIsHeldAndTheSignatureIsWrong()
    {
        GivenTheProviderPublishes(PublicKey(_currentKey, _currentKeyId));

        var outcome = await Verify(Token(signedWith: _rotatedKey, keyId: _currentKeyId));

        outcome.ShouldBeRefusedAsInvalid();
        await _signingKeys.DidNotReceive()
            .RefreshAsync(Arg.Any<ExternalIdentityProviderOptions>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A provider that has never been reachable publishes nothing here, and nothing is what every
    /// token is then checked against. The alternative — accepting a token nobody could verify —
    /// would turn one unreachable dependency into an open door.
    /// </summary>
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

    private Task<ExternalIdentityOutcome> Verify(string idToken, string presentedProvider = _providerName) =>
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

    /// <summary>What a JWKS hands back: the public half, and the identifier the token names it by.</summary>
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
            // Null here means the JSON boolean Google sends; a caller passing "true" is asking for
            // Apple's encoding of the same fact.
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

internal static class ExternalIdentityOutcomeAssertions
{
    /// <summary>
    /// Every refusal is the same refusal, and it carries nothing: the claims of a token that failed
    /// verification are attacker-supplied text, so a caller that reads them has been handed input it
    /// believes is evidence.
    /// </summary>
    internal static void ShouldBeRefusedAsInvalid(this ExternalIdentityOutcome outcome)
    {
        outcome.Status.ShouldBe(ExternalIdentityStatus.InvalidToken);
        outcome.Identity.ShouldBeNull();
    }
}
