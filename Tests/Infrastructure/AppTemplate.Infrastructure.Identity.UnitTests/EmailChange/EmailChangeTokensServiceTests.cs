using AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;
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
/// <see cref="EmailChangeTokens"/> over a real <see cref="UserManager{TUser}"/>, standing in for the
/// named <c>ChangeEmail</c> token provider with the trivial <see cref="StubTokenProvider"/>: what is
/// under test is the adapter's translation of ASP.NET Identity's outcomes, not the token provider
/// itself.
/// </summary>
public sealed class EmailChangeTokensTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();
    private const string _currentPassword = "correct horse battery";
    private const string _storedHash = "stored-hash";
    private const string _newEmail = "new@example.test";

    private readonly IUserEmailStore<AppUser> _store =
        Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
    private readonly IPasswordHasher<AppUser> _passwordHasher = Substitute.For<IPasswordHasher<AppUser>>();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region IssueAsync

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsReportedAsAnIncorrectCurrentPassword()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var request = await CreateTokens().IssueAsync(_userId, _currentPassword, _newEmail, TestToken);

        request.Status.ShouldBe(EmailChangeRequestStatus.IncorrectCurrentPassword);
    }

    [Fact]
    public async Task AWrongCurrentPassword_IsRefused()
    {
        GivenTheAccountExists();
        _passwordHasher.VerifyHashedPassword(Arg.Any<AppUser>(), _storedHash, _currentPassword)
            .Returns(PasswordVerificationResult.Failed);

        var request = await CreateTokens().IssueAsync(_userId, _currentPassword, _newEmail, TestToken);

        request.Status.ShouldBe(EmailChangeRequestStatus.IncorrectCurrentPassword);
    }

    [Fact]
    public async Task ACorrectPasswordAndAnAlreadyTakenAddress_IsSuppressed()
    {
        GivenTheAccountExists();
        GivenThePasswordVerifies();
        // UserManager normalizes the address before it reaches the store, so the stub matches on
        // any string rather than the literal the adapter was given.
        ((IUserEmailStore<AppUser>)_store).FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AppUser { Id = Guid.CreateVersion7(), UserName = "someone-else" });

        var request = await CreateTokens().IssueAsync(_userId, _currentPassword, _newEmail, TestToken);

        request.Status.ShouldBe(EmailChangeRequestStatus.Requested);
        request.Token.ShouldBeNull();
    }

    /// <summary>The suppressed and the issued outcomes must not be distinguishable by shape alone.</summary>
    [Fact]
    public async Task ACorrectPasswordAndAFreeAddress_Issues()
    {
        GivenTheAccountExists();
        GivenThePasswordVerifies();
        ((IUserEmailStore<AppUser>)_store).FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AppUser?)null);

        var request = await CreateTokens().IssueAsync(_userId, _currentPassword, _newEmail, TestToken);

        request.Status.ShouldBe(EmailChangeRequestStatus.Requested);
        request.UserName.ShouldBe("someone");
        request.Token.ShouldBe(StubTokenProvider.Token);
    }

    #endregion

    #region RedeemAsync

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsRefusedOnRedeemToo()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var confirmation = await CreateTokens().RedeemAsync(_userId, _newEmail, "any-token", TestToken);

        confirmation.Status.ShouldBe(EmailChangeConfirmationStatus.NoSuchAccount);
    }

    [Fact]
    public async Task AWrongToken_IsRefusedAsInvalid()
    {
        GivenTheAccountExists();
        GivenTheStoreAcceptsAnUpdate();

        var confirmation = await CreateTokens().RedeemAsync(_userId, _newEmail, "not-the-real-token", TestToken);

        confirmation.Status.ShouldBe(EmailChangeConfirmationStatus.InvalidToken);
    }

    [Fact]
    public async Task ARightToken_Changes()
    {
        GivenTheAccountExists();
        GivenTheStoreAcceptsAnUpdate();

        var confirmation = await CreateTokens()
            .RedeemAsync(_userId, _newEmail, StubTokenProvider.Token, TestToken);

        confirmation.Status.ShouldBe(EmailChangeConfirmationStatus.Changed);
    }

    [Fact]
    public async Task ARightTokenAndARejectedAddress_ReportsTheStoresMessage()
    {
        GivenTheAccountExists();
        GivenTheStoreAcceptsAnUpdate();

        var confirmation = await CreateTokens(rejectNewEmail: true)
            .RedeemAsync(_userId, _newEmail, StubTokenProvider.Token, TestToken);

        confirmation.Status.ShouldBe(EmailChangeConfirmationStatus.Rejected);
        confirmation.RejectionMessage.ShouldNotBeNull().ShouldContain("That address is not allowed.");
    }

    #endregion

    private void GivenTheAccountExists()
    {
        // EmailChangeTokens reads AppUser.PasswordHash directly rather than through the store — see
        // its comment on why, so the store is never asked for it here.
        var user = new AppUser { Id = _userId, UserName = "someone", PasswordHash = _storedHash };
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);
    }

    private void GivenThePasswordVerifies() =>
        _passwordHasher.VerifyHashedPassword(Arg.Any<AppUser>(), _storedHash, _currentPassword)
            .Returns(PasswordVerificationResult.Success);

    private void GivenTheStoreAcceptsAnUpdate() =>
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

    private EmailChangeTokens CreateTokens(bool rejectNewEmail = false) => CreateTokens(out _, rejectNewEmail);

    private EmailChangeTokens CreateTokens(out UserManager<AppUser> userManager, bool rejectNewEmail = false)
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());

        userManager = new UserManager<AppUser>(
            _store,
            options,
            _passwordHasher,
            rejectNewEmail ? [new AlwaysRejectingUserValidator()] : [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        // Stands in for the module's named "EmailChange" provider, registered under the same
        // "Default" name IdentityOptions.Tokens.ChangeEmailTokenProvider defaults to.
        userManager.RegisterTokenProvider("Default", new StubTokenProvider());

        return new EmailChangeTokens(userManager, _directory);
    }

    private sealed class StubTokenProvider : IUserTwoFactorTokenProvider<AppUser>
    {
        public const string Token = "the-change-email-token";

        public Task<string> GenerateAsync(string purpose, UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(Token);

        public Task<bool> ValidateAsync(string purpose, string token, UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(token == Token);

        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// Stands in for the store's own duplicate-address refusal: <c>ChangeEmailAsync</c> runs the
    /// registered <see cref="IUserValidator{TUser}"/>s before persisting, and a real store's default
    /// one is what would reject an address taken since the token was issued.
    /// </summary>
    private sealed class AlwaysRejectingUserValidator : IUserValidator<AppUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(IdentityResult.Failed(
                new IdentityError { Code = "InvalidEmail", Description = "That address is not allowed." }));
    }
}
