using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Infrastructure.Identity.Users;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Users;

/// <summary>
/// <see cref="PasswordResetTokens"/> over a real <see cref="UserManager{TUser}"/>, standing in for the
/// named <c>PasswordResetTokenProvider</c> with a trivial fake: what is under test is the adapter's
/// translation of ASP.NET Identity's outcomes, not the token provider itself.
/// </summary>
public sealed class PasswordResetTokensTests
{
    private const string _knownEmail = "someone@example.test";
    private const string _newPassword = "correct horse battery";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnknownAddress_IssuesNothing()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
        // UserManager normalizes the address before it reaches the store, so the stub matches on any
        // string rather than the literal the adapter was given.
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var pending = await CreateTokens(store, out _).IssueAsync(_knownEmail, TestToken);

        pending.ShouldBeNull();
    }

    [Fact]
    public async Task AKnownAddress_IssuesATokenFromTheNamedProvider()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
        var user = new AppUser { Id = Guid.CreateVersion7(), UserName = "someone" };
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        var pending = await CreateTokens(store, out _).IssueAsync(_knownEmail, TestToken);

        pending.ShouldNotBeNull();
        pending.UserName.ShouldBe("someone");
        pending.Token.ShouldBe(StubTokenProvider.Token);
    }

    [Fact]
    public async Task AnUnknownAddress_IsRefusedOnRedeemToo()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
        // UserManager normalizes the address before it reaches the store, so the stub matches on any
        // string rather than the literal the adapter was given.
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var reset = await CreateTokens(store, out _).RedeemAsync(_knownEmail, "any-token", _newPassword, TestToken);

        reset.Outcome.ShouldBe(PasswordResetOutcome.NoSuchAccount);
    }

    [Fact]
    public async Task AWrongToken_IsRefusedAsInvalid()
    {
        var (store, user) = GivenAKnownAccount();

        var reset = await CreateTokens(store, out _).RedeemAsync(_knownEmail, "not-the-real-token", _newPassword, TestToken);

        reset.Outcome.ShouldBe(PasswordResetOutcome.InvalidToken);
        reset.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task ARightTokenAndAnAcceptablePassword_Resets()
    {
        var (store, user) = GivenAKnownAccount();

        var reset = await CreateTokens(store, out _)
            .RedeemAsync(_knownEmail, StubTokenProvider.Token, _newPassword, TestToken);

        reset.Outcome.ShouldBe(PasswordResetOutcome.Reset);
        reset.UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task ARightTokenAndARejectedPassword_ReportsTheStoresMessage()
    {
        var (store, _) = GivenAKnownAccount();

        var reset = await CreateTokens(store, out _, rejectNewPassword: true)
            .RedeemAsync(_knownEmail, StubTokenProvider.Token, _newPassword, TestToken);

        reset.Outcome.ShouldBe(PasswordResetOutcome.Rejected);
        reset.RejectionMessage.ShouldNotBeNull().ShouldContain("Passwords must have at least one digit.");
    }

    private static (IUserEmailStore<AppUser> Store, AppUser User) GivenAKnownAccount()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
        var user = new AppUser { Id = Guid.CreateVersion7(), UserName = "someone" };

        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        return (store, user);
    }

    private static PasswordResetTokens CreateTokens(
        IUserEmailStore<AppUser> store,
        out UserManager<AppUser> userManager,
        bool rejectNewPassword = false)
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());

        userManager = new UserManager<AppUser>(
            store,
            options,
            Substitute.For<IPasswordHasher<AppUser>>(),
            [],
            rejectNewPassword ? [new AlwaysRejectingPasswordValidator()] : [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        // Stands in for the module's named "PasswordReset" provider, registered under the same
        // "Default" name IdentityOptions.Tokens.PasswordResetTokenProvider defaults to.
        userManager.RegisterTokenProvider("Default", new StubTokenProvider());

        return new PasswordResetTokens(userManager);
    }

    private sealed class StubTokenProvider : IUserTwoFactorTokenProvider<AppUser>
    {
        public const string Token = "the-reset-token";

        public Task<string> GenerateAsync(string purpose, UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(Token);

        public Task<bool> ValidateAsync(string purpose, string token, UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(token == Token);

        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(true);
    }

    private sealed class AlwaysRejectingPasswordValidator : IPasswordValidator<AppUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password) =>
            Task.FromResult(IdentityResult.Failed(
                new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit." }));
    }
}
