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

public sealed class EmailTokensServiceEmailChangeTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();
    private const string _currentPassword = "correct horse battery";
    private const string _storedHash = "stored-hash";
    private const string _newEmail = "new@example.test";
    private const string _currentEmail = "current@example.test";

    private readonly IUserEmailStore<AppUser> _store =
        Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
    private readonly IPasswordHasher<AppUser> _passwordHasher = Substitute.For<IPasswordHasher<AppUser>>();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsReportedAsSuch()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var request = await CreateTokens().IssueEmailChangeAsync(_userId, _currentPassword, _newEmail, TestToken);

        request.Status.ShouldBe(EmailChangeRequestStatus.NoSuchAccount);
        request.Token.ShouldBeNull();
    }

    [Fact]
    public async Task AWrongCurrentPassword_IsRefused()
    {
        GivenTheAccountExists();
        _passwordHasher.VerifyHashedPassword(Arg.Any<AppUser>(), _storedHash, _currentPassword)
            .Returns(PasswordVerificationResult.Failed);

        var request = await CreateTokens().IssueEmailChangeAsync(_userId, _currentPassword, _newEmail, TestToken);

        request.Status.ShouldBe(EmailChangeRequestStatus.IncorrectCurrentPassword);
    }

    [Fact]
    public async Task ACorrectPasswordAndAnAlreadyTakenAddress_IsReportedAsSuch_WithNoToken()
    {
        GivenTheAccountExists();
        GivenThePasswordVerifies();
        ((IUserEmailStore<AppUser>)_store).FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AppUser { Id = Guid.CreateVersion7(), UserName = "someone-else" });

        var request = await CreateTokens().IssueEmailChangeAsync(_userId, _currentPassword, _newEmail, TestToken);

        request.Status.ShouldBe(EmailChangeRequestStatus.AddressAlreadyRegistered);
        request.Token.ShouldBeNull();
    }

    [Fact]
    public async Task ACorrectPasswordAndAFreeAddress_IsReportedAsRequested_WithTheToken()
    {
        GivenTheAccountExists();
        GivenThePasswordVerifies();
        ((IUserEmailStore<AppUser>)_store).FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AppUser?)null);

        var request = await CreateTokens().IssueEmailChangeAsync(_userId, _currentPassword, _newEmail, TestToken);

        request.Status.ShouldBe(EmailChangeRequestStatus.Requested);
        request.Token.ShouldBe(StubTokenProvider.Token);
    }

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsRefusedOnRedeemAsAnInvalidToken()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var confirmation = await CreateTokens().ChangeEmailAsync(_userId, _newEmail, "any-token", TestToken);

        confirmation.Status.ShouldBe(EmailChangeStatus.InvalidToken);
    }

    [Fact]
    public async Task AWrongToken_IsRefusedAsInvalid()
    {
        GivenTheAccountExists();
        GivenTheStoreAcceptsAnUpdate();

        var confirmation = await CreateTokens().ChangeEmailAsync(_userId, _newEmail, "not-the-real-token", TestToken);

        confirmation.Status.ShouldBe(EmailChangeStatus.InvalidToken);
    }

    [Fact]
    public async Task ARightToken_Changes()
    {
        GivenTheAccountExists();
        GivenTheStoreAcceptsAnUpdate();

        var confirmation = await CreateTokens()
            .ChangeEmailAsync(_userId, _newEmail, StubTokenProvider.Token, TestToken);

        confirmation.Status.ShouldBe(EmailChangeStatus.Changed);
    }

    [Fact]
    public async Task ARightToken_MovesTheUserNameWithTheAddressInTheSameUpdate()
    {
        var user = GivenTheAccountExists();
        GivenTheStoreAcceptsAnUpdate();

        await CreateTokens().ChangeEmailAsync(_userId, _newEmail, StubTokenProvider.Token, TestToken);

        user.UserName.ShouldBe(_newEmail);
        await _store.Received(1).UpdateAsync(
            Arg.Is<AppUser>(updated => updated.UserName == _newEmail),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AWrongToken_LeavesTheUserNameAlone()
    {
        var user = GivenTheAccountExists();
        GivenTheStoreAcceptsAnUpdate();

        await CreateTokens().ChangeEmailAsync(_userId, _newEmail, "not-the-real-token", TestToken);

        user.UserName.ShouldBe(_currentEmail);
        await _store.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARightTokenAndARejectedAddress_ReportsTheStoresMessage()
    {
        GivenTheAccountExists();
        GivenTheStoreAcceptsAnUpdate();

        var confirmation = await CreateTokens(rejectNewEmail: true)
            .ChangeEmailAsync(_userId, _newEmail, StubTokenProvider.Token, TestToken);

        confirmation.Status.ShouldBe(EmailChangeStatus.Rejected);
        confirmation.RejectionMessage.ShouldNotBeNull().ShouldContain("That address is not allowed.");
    }

    private AppUser GivenTheAccountExists()
    {
        var user = new AppUser { Id = _userId, UserName = _currentEmail, Email = _currentEmail, PasswordHash = _storedHash };
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);

        return user;
    }

    private void GivenThePasswordVerifies() =>
        _passwordHasher.VerifyHashedPassword(Arg.Any<AppUser>(), _storedHash, _currentPassword)
            .Returns(PasswordVerificationResult.Success);

    private void GivenTheStoreAcceptsAnUpdate() =>
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

    private EmailTokensService CreateTokens(bool rejectNewEmail = false) => CreateTokens(out _, rejectNewEmail);

    private EmailTokensService CreateTokens(out UserManager<AppUser> userManager, bool rejectNewEmail = false)
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

        userManager.RegisterTokenProvider("Default", new StubTokenProvider());

        return new EmailTokensService(userManager, _directory);
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

    private sealed class AlwaysRejectingUserValidator : IUserValidator<AppUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(IdentityResult.Failed(
                new IdentityError { Code = "InvalidEmail", Description = "That address is not allowed." }));
    }
}
