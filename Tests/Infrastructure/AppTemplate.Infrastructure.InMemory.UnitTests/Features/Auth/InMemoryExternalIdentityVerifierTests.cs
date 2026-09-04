using AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Infrastructure.InMemory.Features.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Auth;

/// <summary>
/// The double a test host uses in place of the adapter that talks to Google, Microsoft or Apple.
/// <para>
/// What is under test is that it answers exactly what was arranged and refuses everything else — a
/// double that verified anything it had not been told about would let a test pass while the use case
/// above it accepted a token nobody vouched for.
/// </para>
/// <para>
/// Reached through the port from a composed container, the same way every other double in this
/// project is: the class behind it is internal, and the registration is part of what is under test.
/// </para>
/// </summary>
public sealed class InMemoryExternalIdentityVerifierTests : IDisposable
{
    private const string _provider = "google";
    private const string _idToken = "an-id-token";

    private static readonly VerifiedExternalIdentity _identity =
        new(_provider, "108412345678901234567", "someone@example.com", EmailVerified: true);

    private readonly ServiceProvider _host = new ServiceCollection()
        .AddInMemoryExternalIdentities()
        .BuildServiceProvider(validateScopes: true);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private AcceptedExternalIdentities Accepted => _host.GetRequiredService<AcceptedExternalIdentities>();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task VerifyAsync_ReturnsTheIdentityThatWasArranged()
    {
        Accepted.Accept(_provider, _idToken, _identity);

        var outcome = await Verify(_provider, _idToken);

        outcome.Status.ShouldBe(ExternalIdentityStatus.Verified);
        outcome.Identity.ShouldBe(_identity);
    }

    [Fact]
    public async Task VerifyAsync_RefusesATokenNobodyArranged()
    {
        var outcome = await Verify(_provider, _idToken);

        outcome.Status.ShouldBe(ExternalIdentityStatus.InvalidToken);
        outcome.Identity.ShouldBeNull();
    }

    /// <summary>
    /// The property the real adapter enforces by checking the issuer: a token minted by one provider
    /// cannot be presented as another's. A double keyed on the token alone would stop covering it.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_RefusesATokenPresentedForADifferentProvider()
    {
        Accepted.Accept(_provider, _idToken, _identity);

        (await Verify("apple", _idToken)).Status.ShouldBe(ExternalIdentityStatus.InvalidToken);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsTheRefusalThatWasArrangedByName()
    {
        Accepted.Refuse(_provider, _idToken, ExternalIdentityStatus.UnknownProvider);

        var outcome = await Verify(_provider, _idToken);

        outcome.Status.ShouldBe(ExternalIdentityStatus.UnknownProvider);
        outcome.Identity.ShouldBeNull();
    }

    /// <summary>
    /// A refusal carrying an identity would be the double contradicting the port it stands for, and
    /// the caller reads that identity the moment the status is not one it recognises as a refusal.
    /// </summary>
    [Fact]
    public void Refuse_WillNotBeUsedToArrangeAVerification()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => Accepted.Refuse(_provider, _idToken, ExternalIdentityStatus.Verified));
    }

    [Fact]
    public async Task Clear_ForgetsWhatAPreviousTestArranged()
    {
        Accepted.Accept(_provider, _idToken, _identity);

        Accepted.Clear();

        (await Verify(_provider, _idToken)).Status.ShouldBe(ExternalIdentityStatus.InvalidToken);
    }

    [Fact]
    public async Task VerifyAsync_ObservesCancellationOnEntry()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        using var scope = _host.CreateScope();

        await Should.ThrowAsync<OperationCanceledException>(
            () => scope.ServiceProvider
                .GetRequiredService<IExternalIdentityVerifier>()
                .VerifyAsync(_provider, _idToken, cancelled.Token));
    }

    private async Task<ExternalIdentityOutcome> Verify(string provider, string idToken)
    {
        using var scope = _host.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IExternalIdentityVerifier>()
            .VerifyAsync(provider, idToken, TestToken);
    }
}
