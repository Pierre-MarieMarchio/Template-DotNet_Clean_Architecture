using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.TwoFactor.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.TwoFactor.Configuration.Options;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.TwoFactor.Adapters.Services;

public sealed class TwoFactorServiceChallengeTests
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

    public TwoFactorServiceChallengeTests() => _dateTimeProvider.UtcNow.Returns(_now);

    [Fact]
    public async Task IssueChallengeAsync_EmbedsTheUserIdInTheChallengeToken()
    {
        GivenTheAccountExists();

        var issued = await CreateChallenges().IssueChallengeAsync(_userId, TestToken);

        issued.ChallengeToken.ShouldStartWith($"{_userId:N}.");
    }

    [Fact]
    public async Task IssueChallengeAsync_SetsTheExpiryFromTheConfiguredLifetime()
    {
        GivenTheAccountExists();

        var issued = await CreateChallenges(TimeSpan.FromMinutes(5)).IssueChallengeAsync(_userId, TestToken);

        issued.ExpiresAt.ShouldBe(_now.AddMinutes(5));
    }

    [Theory]
    [InlineData("no-dot-at-all")]
    [InlineData("not-a-guid.some-secret")]
    [InlineData("")]
    public async Task RedeemChallengeAsync_AMalformedToken_IsRejected(string malformed)
    {
        var redemption = await CreateChallenges().RedeemChallengeAsync(malformed, "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemChallengeAsync_AnAccountThatNoLongerExists_IsRejected()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var redemption = await CreateChallenges().RedeemChallengeAsync($"{_userId:N}.some-secret", "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemChallengeAsync_BeforeAnyChallengeWasIssued_IsRejected()
    {
        GivenTheAccountExists();

        var redemption = await CreateChallenges().RedeemChallengeAsync($"{_userId:N}.some-secret", "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemChallengeAsync_WithATamperedSecret_IsRejected()
    {
        var challenges = CreateChallenges();
        GivenTheAccountExists();
        var issued = await challenges.IssueChallengeAsync(_userId, TestToken);

        var redemption = await challenges.RedeemChallengeAsync($"{_userId:N}.not-the-real-secret", "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
        issued.ChallengeToken.ShouldNotContain("not-the-real-secret");
    }

    [Fact]
    public async Task RedeemChallengeAsync_AfterTheConfiguredLifetimeHasPassed_IsRejected()
    {
        var challenges = CreateChallenges(TimeSpan.FromMinutes(5));
        GivenTheAccountExists();
        var issued = await challenges.IssueChallengeAsync(_userId, TestToken);

        _dateTimeProvider.UtcNow.Returns(_now.AddMinutes(5));

        var redemption = await challenges.RedeemChallengeAsync(issued.ChallengeToken, "123456", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemChallengeAsync_ARightAuthenticatorCode_IsVerified()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueChallengeAsync(_userId, TestToken);

        var redemption = await challenges.RedeemChallengeAsync(
            issued.ChallengeToken,
            AuthenticatorCodes.CurrentCodeFor(sharedKey),
            TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.Verified);
        redemption.Account!.UserId.ShouldBe(_userId);
        redemption.UsedRecoveryCode.ShouldBeFalse();
    }

    [Fact]
    public async Task RedeemChallengeAsync_ARightCode_ConsumesTheChallenge()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueChallengeAsync(_userId, TestToken);
        string code = AuthenticatorCodes.CurrentCodeFor(sharedKey);

        await challenges.RedeemChallengeAsync(issued.ChallengeToken, code, TestToken);
        var replay = await challenges.RedeemChallengeAsync(issued.ChallengeToken, code, TestToken);

        replay.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidChallenge);
    }

    [Fact]
    public async Task RedeemChallengeAsync_ARightRecoveryCode_IsVerifiedAndFlagged()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        GivenAnAuthenticatorKey(user);
        _recoveryCodes = ["ABCDE-12345", "FGHIJ-67890"];
        var issued = await challenges.IssueChallengeAsync(_userId, TestToken);

        var redemption = await challenges.RedeemChallengeAsync(issued.ChallengeToken, "ABCDE-12345", TestToken);

        redemption.Status.ShouldBe(TwoFactorRedemptionStatus.Verified);
        redemption.UsedRecoveryCode.ShouldBeTrue();
    }

    [Fact]
    public async Task RedeemChallengeAsync_ARightRecoveryCode_CannotBeReplayedOnANewChallenge()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        GivenAnAuthenticatorKey(user);
        _recoveryCodes = ["ABCDE-12345"];
        var firstChallenge = await challenges.IssueChallengeAsync(_userId, TestToken);
        await challenges.RedeemChallengeAsync(firstChallenge.ChallengeToken, "ABCDE-12345", TestToken);

        var secondChallenge = await challenges.IssueChallengeAsync(_userId, TestToken);
        var replay = await challenges.RedeemChallengeAsync(secondChallenge.ChallengeToken, "ABCDE-12345", TestToken);

        replay.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidCode);
    }

    [Fact]
    public async Task RedeemChallengeAsync_AWrongCode_LeavesTheChallengeLiveForARetry()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueChallengeAsync(_userId, TestToken);

        var wrong = await challenges.RedeemChallengeAsync(issued.ChallengeToken, "000000", TestToken);
        wrong.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidCode);

        var right = await challenges.RedeemChallengeAsync(issued.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);
        right.Status.ShouldBe(TwoFactorRedemptionStatus.Verified);
    }

    [Fact]
    public async Task RedeemChallengeAsync_AfterTheConfiguredNumberOfWrongCodes_DestroysTheChallenge()
    {
        var challenges = CreateChallenges(maxChallengeAttempts: 3);
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueChallengeAsync(_userId, TestToken);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var refused = await challenges.RedeemChallengeAsync(issued.ChallengeToken, "000000", TestToken);

            refused.Status.ShouldBe(
                TwoFactorRedemptionStatus.InvalidCode,
                $"attempt {attempt} of 3 is a wrong code against a live challenge, which is a wrong " +
                "code rather than a spent challenge — the two answer identically to a client and " +
                "must not answer identically here.");
        }

        _tokens.ShouldBeEmpty(
            "the third wrong code took the last attempt, so the challenge must already be gone from " +
            "the store rather than waiting to be refused on presentation.");

        var afterTheCeiling = await challenges.RedeemChallengeAsync(
            issued.ChallengeToken,
            AuthenticatorCodes.CurrentCodeFor(sharedKey),
            TestToken);

        afterTheCeiling.Status.ShouldBe(
            TwoFactorRedemptionStatus.InvalidChallenge,
            "the challenge has spent its attempts, so even the right code must not redeem it. " +
            "Anything else leaves the six-digit space open to whoever already has the password.");
    }

    [Fact]
    public async Task RedeemChallengeAsync_AWrongCode_DoesNotExtendTheChallengesLife()
    {
        var challenges = CreateChallenges(TimeSpan.FromMinutes(5));
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var issued = await challenges.IssueChallengeAsync(_userId, TestToken);

        var wrong = await challenges.RedeemChallengeAsync(issued.ChallengeToken, "000000", TestToken);
        wrong.Status.ShouldBe(TwoFactorRedemptionStatus.InvalidCode);

        _dateTimeProvider.UtcNow.Returns(_now.AddMinutes(5));

        var afterExpiry = await challenges.RedeemChallengeAsync(
            issued.ChallengeToken,
            AuthenticatorCodes.CurrentCodeFor(sharedKey),
            TestToken);

        afterExpiry.Status.ShouldBe(
            TwoFactorRedemptionStatus.InvalidChallenge,
            "the deadline is the one the challenge was issued with. A wrong code that reset it would " +
            "make the lifetime a value an attacker controls.");
    }

    [Fact]
    public async Task IssueChallengeAsync_CalledAgain_SupersedesThePreviousChallenge()
    {
        var challenges = CreateChallenges();
        var user = GivenTheAccountExists();
        string sharedKey = GivenAnAuthenticatorKey(user);
        var first = await challenges.IssueChallengeAsync(_userId, TestToken);
        await challenges.IssueChallengeAsync(_userId, TestToken);

        var redemption = await challenges.RedeemChallengeAsync(first.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

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
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        byte[] bytes = new byte[20];
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

    private TwoFactorService CreateChallenges(
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

        return new TwoFactorService(userManager, _directory, _dateTimeProvider, options);
    }
}
