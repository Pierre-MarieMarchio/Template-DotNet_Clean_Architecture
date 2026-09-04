using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using AppTemplate.Infrastructure.Identity.Features.Auth.Services;
using AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Features.Auth.Services;

/// <summary>
/// <see cref="TwoFactorChallengeService"/> over a real <see cref="UserManager{TUser}"/>: whether a challenge
/// is genuinely single-use, superseding, self-describing and time-limited — the four properties
/// <c>ITwoFactorChallengeService</c> promises — and whether a right code from either the authenticator app
/// or a recovery code is actually accepted.
/// </summary>
public sealed class TwoFactorChallengeServiceTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddYears(5);

    private readonly IUserStore<AppUser> _store = (IUserStore<AppUser>)Substitute.For(
        [
            typeof(IUserStore<AppUser>),
            typeof(IUserTwoFactorStore<AppUser>),
            typeof(IUserAuthenticatorKeyStore<AppUser>),
            typeof(IUserTwoFactorRecoveryCodeStore<AppUser>),
            typeof(IUserAuthenticationTokenStore<AppUser>),
        ],
        []);

    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();
    private readonly Dictionary<string, string> _tokens = [];
    private List<string> _recoveryCodes = [];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public TwoFactorChallengeServiceTests() => _dateTimeProvider.UtcNow.Returns(_now);

    [Fact]
    public async Task IssueAsync_EmbedsTheUserIdInTheChallengeToken()
    {
        GivenTheAccountExists();

        var issued = await CreateChallenges().IssueAsync(_userId, TestToken);

        issued.ChallengeToken.ShouldStartWith($"{_userId:N}.");
    }

    [Fact]
    public async Task IssueAsync_SetsTheExpiryFromTheConfiguredLifetime()
    {
        GivenTheAccountExists();

        var issued = await CreateChallenges(TimeSpan.FromMinutes(5)).IssueAsync(_userId, TestToken);

        issued.ExpiresAt.ShouldBe(_now.AddMinutes(5));
    }

    [Theory]
    [InlineData("no-dot-at-all")]
    [InlineData("not-a-guid.some-secret")]
    [InlineData("")]
    public async Task RedeemAsync_AMalformedToken_IsRejected(string malformed)
    {
        var redemption = await CreateChallenges().RedeemAsync(malformed, "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemAsync_AnAccountThatNoLongerExists_IsRejected()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var redemption = await CreateChallenges().RedeemAsync($"{_userId:N}.some-secret", "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemAsync_BeforeAnyChallengeWasIssued_IsRejected()
    {
        GivenTheAccountExists();

        var redemption = await CreateChallenges().RedeemAsync($"{_userId:N}.some-secret", "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    /// <summary>The secret is a bearer credential: swapping it for another user's does not help either.</summary>
    [Fact]
    public async Task RedeemAsync_WithATamperedSecret_IsRejected()
    {
        var challenges = CreateChallenges();
        GivenTheAccountExists();
        var issued = await challenges.IssueAsync(_userId, TestToken);

        var redemption = await challenges.RedeemAsync($"{_userId:N}.not-the-real-secret", "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
        issued.ChallengeToken.ShouldNotContain("not-the-real-secret");
    }

    [Fact]
    public async Task RedeemAsync_AfterTheConfiguredLifetimeHasPassed_IsRejected()
    {
        var challenges = CreateChallenges(TimeSpan.FromMinutes(5));
        GivenTheAccountExists();
        var issued = await challenges.IssueAsync(_userId, TestToken);

        _dateTimeProvider.UtcNow.Returns(_now.AddMinutes(5));

        var redemption = await challenges.RedeemAsync(issued.ChallengeToken, "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemAsync_ARightAuthenticatorCode_IsVerified()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueAsync(_userId, TestToken);

        var redemption = await challenges.RedeemAsync(
            issued.ChallengeToken,
            AuthenticatorCodes.CurrentCodeFor(sharedKey),
            TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.Verified);
        redemption.Account!.UserId.ShouldBe(_userId);
        redemption.UsedRecoveryCode.ShouldBeFalse();
    }

    /// <summary>Single-use: the same challenge cannot mint a second token pair.</summary>
    [Fact]
    public async Task RedeemAsync_ARightCode_ConsumesTheChallenge()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueAsync(_userId, TestToken);
        string code = AuthenticatorCodes.CurrentCodeFor(sharedKey);

        await challenges.RedeemAsync(issued.ChallengeToken, code, TestToken);
        var replay = await challenges.RedeemAsync(issued.ChallengeToken, code, TestToken);

        replay.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemAsync_ARightRecoveryCode_IsVerifiedAndFlagged()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        GivenAnAuthenticatorKey(user);
        _recoveryCodes = ["ABCDE-12345", "FGHIJ-67890"];
        var issued = await challenges.IssueAsync(_userId, TestToken);

        var redemption = await challenges.RedeemAsync(issued.ChallengeToken, "ABCDE-12345", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.Verified);
        redemption.UsedRecoveryCode.ShouldBeTrue();
    }

    /// <summary>Recovery codes are single-use: redeeming one removes it from the set.</summary>
    [Fact]
    public async Task RedeemAsync_ARightRecoveryCode_CannotBeReplayedOnANewChallenge()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        GivenAnAuthenticatorKey(user);
        _recoveryCodes = ["ABCDE-12345"];
        var firstChallenge = await challenges.IssueAsync(_userId, TestToken);
        await challenges.RedeemAsync(firstChallenge.ChallengeToken, "ABCDE-12345", TestToken);

        var secondChallenge = await challenges.IssueAsync(_userId, TestToken);
        var replay = await challenges.RedeemAsync(secondChallenge.ChallengeToken, "ABCDE-12345", TestToken);

        replay.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidCode);
    }

    /// <summary>
    /// A mistyped code should not force the caller back through <c>/login</c> — up to the ceiling the
    /// case below asserts.
    /// </summary>
    [Fact]
    public async Task RedeemAsync_AWrongCode_LeavesTheChallengeLiveForARetry()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueAsync(_userId, TestToken);

        var wrong = await challenges.RedeemAsync(issued.ChallengeToken, "000000", TestToken);
        wrong.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidCode);

        var right = await challenges.RedeemAsync(issued.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);
        right.Status.ShouldBe(TwoFactorRedemptionStatus.Verified);
    }

    /// <summary>
    /// The ceiling, and the only thing that bounds guessing a code.
    /// </summary>
    /// <remarks>
    /// Account lockout counts failed <em>password</em> checks — <c>CheckPasswordSignInAsync</c> with
    /// <c>lockoutOnFailure</c> — and presenting a code is not one. Without this counter a caller
    /// holding the password could offer codes for the whole challenge lifetime, bounded only by a
    /// rate limiter that is per process and therefore per replica. The right code is presented
    /// last on purpose: what has to be refused is the challenge itself, not the code.
    /// </remarks>
    [Fact]
    public async Task RedeemAsync_AfterTheConfiguredNumberOfWrongCodes_DestroysTheChallenge()
    {
        var challenges = CreateChallenges(maxChallengeAttempts: 3);
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueAsync(_userId, TestToken);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var refused = await challenges.RedeemAsync(issued.ChallengeToken, "000000", TestToken);

            refused.Status.ShouldBe(
                TwoFactorRedemptionStatus.InvalidCode,
                $"attempt {attempt} of 3 is a wrong code against a live challenge, which is a wrong " +
                "code rather than a spent challenge — the two answer identically to a client and " +
                "must not answer identically here.");
        }

        // Asserted before another redemption is attempted, and that ordering is the point: the
        // challenge has to be destroyed by the write that reached the ceiling, not merely refused by
        // the next call to arrive. A row left behind is a row that answers one more guess.
        _tokens.ShouldBeEmpty(
            "the third wrong code took the last attempt, so the challenge must already be gone from " +
            "the store rather than waiting to be refused on presentation.");

        var afterTheCeiling = await challenges.RedeemAsync(
            issued.ChallengeToken,
            AuthenticatorCodes.CurrentCodeFor(sharedKey),
            TestToken);

        afterTheCeiling.Status.ShouldBe(
            TwoFactorRedemptionStatus.InvalidChallenge,
            "the challenge has spent its attempts, so even the right code must not redeem it. " +
            "Anything else leaves the six-digit space open to whoever already has the password.");
    }

    /// <summary>
    /// Spending an attempt rewrites the challenge, and must carry its deadline over untouched.
    /// </summary>
    /// <remarks>
    /// The counter lives in the same stored value as the expiry, so the write that records a wrong
    /// code is also the write that could extend one — which would turn guessing into a way of keeping
    /// a challenge alive for ever, one wrong code at a time.
    /// </remarks>
    [Fact]
    public async Task RedeemAsync_AWrongCode_DoesNotExtendTheChallengesLife()
    {
        var challenges = CreateChallenges(TimeSpan.FromMinutes(5));
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueAsync(_userId, TestToken);

        var wrong = await challenges.RedeemAsync(issued.ChallengeToken, "000000", TestToken);
        wrong.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidCode);

        _dateTimeProvider.UtcNow.Returns(_now.AddMinutes(5));

        var afterExpiry = await challenges.RedeemAsync(
            issued.ChallengeToken,
            AuthenticatorCodes.CurrentCodeFor(sharedKey),
            TestToken);

        afterExpiry.Status.ShouldBe(
            TwoFactorRedemptionStatus.InvalidChallenge,
            "the deadline is the one the challenge was issued with. A wrong code that reset it would " +
            "make the lifetime a value an attacker controls.");
    }

    /// <summary>Only the latest challenge for an account is ever live — see <c>ITwoFactorChallengeService</c>.</summary>
    [Fact]
    public async Task IssueAsync_CalledAgain_SupersedesThePreviousChallenge()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var first = await challenges.IssueAsync(_userId, TestToken);
        await challenges.IssueAsync(_userId, TestToken);

        var redemption = await challenges.RedeemAsync(first.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    private string GivenAnAuthenticatorKey(AppUser user)
    {
        string sharedKey = GenerateBase32Key();

        ((IUserAuthenticatorKeyStore<AppUser>)_store)
            .GetAuthenticatorKeyAsync(user, Arg.Any<CancellationToken>())
            .Returns(sharedKey);

        return sharedKey;
    }

    private static string GenerateBase32Key()
    {
        // Twenty random bytes, base32-encoded by hand with the RFC 4648 alphabet — the same shape
        // ResetAuthenticatorKeyAsync produces. Encoding is not RFC 6238: it carries no cryptographic
        // meaning of its own, only how the random bytes below are spelled.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new byte[20];
        Random.Shared.NextBytes(bytes);

        var builder = new System.Text.StringBuilder();
        int buffer = 0;
        int bitsLeft = 0;

        foreach (byte b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                builder.Append(alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
        {
            builder.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return builder.ToString();
    }

    private AppUser GivenTheAccountExists()
    {
        var user = new AppUser
        {
            Id = _userId,
            UserName = "someone",
            Email = "someone@identity.test",
            TwoFactorEnabled = true,
        };

        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        var twoFactorStore = (IUserTwoFactorStore<AppUser>)_store;
        twoFactorStore.GetTwoFactorEnabledAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((AppUser)callInfo[0]!).TwoFactorEnabled));

        var recoveryCodeStore = (IUserTwoFactorRecoveryCodeStore<AppUser>)_store;
        recoveryCodeStore.RedeemCodeAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(_recoveryCodes.Remove((string)callInfo[1]!)));

        var tokenStore = (IUserAuthenticationTokenStore<AppUser>)_store;
        tokenStore.SetTokenAsync(
                Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _tokens[$"{callInfo[1]}:{callInfo[2]}"] = (string)callInfo[3]!;
                return Task.CompletedTask;
            });
        tokenStore.GetTokenAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(
                _tokens.TryGetValue($"{callInfo[1]}:{callInfo[2]}", out string? value) ? value : null));
        tokenStore.RemoveTokenAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _tokens.Remove($"{callInfo[1]}:{callInfo[2]}");
                return Task.CompletedTask;
            });

        return user;
    }

    private TwoFactorChallengeService CreateChallenges(
        TimeSpan? challengeLifetime = null,
        int maxChallengeAttempts = 5)
    {
        var userManager = new UserManager<AppUser>(
            _store,
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            Substitute.For<IPasswordHasher<AppUser>>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        userManager.RegisterTokenProvider(TokenOptions.DefaultAuthenticatorProvider, new AuthenticatorTokenProvider<AppUser>());

        var options = new OptionsWrapper<TwoFactorOptions>(
            new TwoFactorOptions
            {
                ChallengeLifetime = challengeLifetime ?? TimeSpan.FromMinutes(5),
                MaxChallengeAttempts = maxChallengeAttempts,
            });

        return new TwoFactorChallengeService(userManager, _directory, _dateTimeProvider, options);
    }
}
