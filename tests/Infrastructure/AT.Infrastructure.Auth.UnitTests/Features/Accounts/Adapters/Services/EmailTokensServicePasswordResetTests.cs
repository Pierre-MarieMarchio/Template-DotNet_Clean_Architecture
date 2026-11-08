using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

public sealed class EmailTokensServicePasswordResetTests
{
    private const string _knownEmail = "someone@example.test";
    private const string _newPassword = "correct horse battery";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnknownAddress_IssuesNothing()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        string? pending = await CreateTokens(store, out _).IssuePasswordResetAsync(_knownEmail, TestToken);

        pending.ShouldBeNull();
    }

    [Fact]
    public async Task AKnownAddress_IssuesATokenFromTheNamedProvider()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
        var user = new AppUser { Id = Guid.CreateVersion7(), UserName = "someone" };
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        string? token = await CreateTokens(store, out _).IssuePasswordResetAsync(_knownEmail, TestToken);

        token.ShouldBe(StubTokenProvider.Token);
    }

    [Fact]
    public async Task AnUnknownAddress_IsRefusedOnRedeemAsAnInvalidToken()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var reset = await CreateTokens(store, out _).ResetPasswordAsync(_knownEmail, "any-token", _newPassword, TestToken);

        reset.Status.ShouldBe(PasswordResetStatus.InvalidToken);
        reset.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task AWrongToken_IsRefusedAsInvalid()
    {
        var (store, user) = GivenAKnownAccount();

        var reset = await CreateTokens(store, out _).ResetPasswordAsync(_knownEmail, "not-the-real-token", _newPassword, TestToken);

        reset.Status.ShouldBe(PasswordResetStatus.InvalidToken);
        reset.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task ARightTokenAndAnAcceptablePassword_Resets()
    {
        var (store, user) = GivenAKnownAccount();

        var reset = await CreateTokens(store, out _)
            .ResetPasswordAsync(_knownEmail, StubTokenProvider.Token, _newPassword, TestToken);

        reset.Status.ShouldBe(PasswordResetStatus.Reset);
        reset.UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task ARightTokenAndARejectedPassword_ReportsTheStoresMessage()
    {
        var (store, _) = GivenAKnownAccount();

        var reset = await CreateTokens(store, out _, rejectNewPassword: true)
            .ResetPasswordAsync(_knownEmail, StubTokenProvider.Token, _newPassword, TestToken);

        reset.Status.ShouldBe(PasswordResetStatus.Rejected);
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

    private static EmailTokensService CreateTokens(
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

        userManager.RegisterTokenProvider("Default", new StubTokenProvider());

        return new EmailTokensService(userManager, Substitute.For<IAppUserDirectory>());
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
