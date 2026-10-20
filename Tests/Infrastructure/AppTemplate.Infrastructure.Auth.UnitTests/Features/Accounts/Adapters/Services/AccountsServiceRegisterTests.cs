using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

public sealed class AccountsServiceRegisterTests
{
    private const string _email = "someone@identity.test";
    private const string _normalizedEmail = "SOMEONE@IDENTITY.TEST";
    private const string _password = "Correct!Horse9";

    private readonly IUserEmailStore<AppUser> _store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
    private readonly IdentityOptions _identityOptions = new()
    {
        User = { RequireUniqueEmail = true, AllowedUserNameCharacters = string.Empty },
    };
    private readonly List<IUserValidator<AppUser>> _userValidators = [new UserValidator<AppUser>()];
    private readonly RecordingPasswordHasher _hasher = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public AccountsServiceRegisterTests()
    {
        _store.GetUserIdAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ((AppUser)callInfo[0]!).Id.ToString());
        _store.GetUserNameAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ((AppUser)callInfo[0]!).UserName);
        _store.GetEmailAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ((AppUser)callInfo[0]!).Email);
        _store.FindByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);
        _store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);
        _store.CreateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);
    }

    [Fact]
    public async Task AnUnknownAddress_IsCreatedWithTheAddressAsItsUserName()
    {
        var registration = await CreateAccounts().RegisterAsync(_email, _password, TestToken);

        registration.Status.ShouldBe(AccountRegistrationStatus.Created);
        await _store.Received(1).CreateAsync(
            Arg.Is<AppUser>(user => user.UserName == _email && user.Email == _email && !user.EmailConfirmed),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATakenAddress_IsAlreadyRegistered_WithItsConfirmationState_AndNothingIsCreated(bool confirmed)
    {
        GivenTheAddressIsTaken(confirmed);

        var registration = await CreateAccounts().RegisterAsync(_email, _password, TestToken);

        registration.ShouldBe(AccountRegistrationOutcome.AlreadyRegistered(confirmed));
        await _store.DidNotReceive().CreateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EitherBranch_HashesTheSubmittedPasswordExactlyOnce(bool taken)
    {
        if (taken)
        {
            GivenTheAddressIsTaken(confirmed: true);
        }

        await CreateAccounts().RegisterAsync(_email, _password, TestToken);

        _hasher.Hashes.ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWeakPassword_IsTheSameRejectionWhetherOrNotTheAddressIsTaken(bool taken)
    {
        if (taken)
        {
            GivenTheAddressIsTaken(confirmed: true);
        }

        var registration = await CreateAccounts().RegisterAsync(_email, "short", TestToken);

        registration.Status.ShouldBe(AccountRegistrationStatus.Rejected);
        registration.EmailRejection.ShouldBeNull();
        registration.PasswordRejection.ShouldNotBeNullOrEmpty();
        _hasher.Hashes.ShouldBe(0);
        await _store.DidNotReceive().CreateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALostRaceReportedByTheValidator_IsAlreadyRegistered()
    {
        _store.FindByEmailAsync(_normalizedEmail, Arg.Any<CancellationToken>())
            .Returns(null, null, ExistingAccount(confirmed: false));
        _store.CreateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(_email)));

        var registration = await CreateAccounts().RegisterAsync(_email, _password, TestToken);

        registration.ShouldBe(AccountRegistrationOutcome.AlreadyRegistered(emailConfirmed: false));
    }

    [Fact]
    public async Task ALostRaceOnTheUniqueIndex_IsAlreadyRegistered()
    {
        _store.FindByEmailAsync(_normalizedEmail, Arg.Any<CancellationToken>())
            .Returns(null, null, null, ExistingAccount(confirmed: false));
        _store.CreateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException(
                "duplicate key",
                new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)));

        var registration = await CreateAccounts().RegisterAsync(_email, _password, TestToken);

        registration.ShouldBe(AccountRegistrationOutcome.AlreadyRegistered(emailConfirmed: false));
    }

    [Fact]
    public async Task AnyOtherStoreFailure_Propagates()
    {
        _store.CreateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("the database is gone"));

        await Should.ThrowAsync<DbUpdateException>(() => CreateAccounts().RegisterAsync(_email, _password, TestToken));
    }

    [Fact]
    public async Task ACodeThatMerelyContainsDuplicate_IsARejection()
    {
        _userValidators.Add(new RefusingValidator(new IdentityError
        {
            Code = "DuplicatePhoneNumber",
            Description = "That phone number is already in use.",
        }));

        var registration = await CreateAccounts().RegisterAsync(_email, _password, TestToken);

        registration.Status.ShouldBe(AccountRegistrationStatus.Rejected);
        registration.PasswordRejection.ShouldBe("That phone number is already in use.");
    }

    [Fact]
    public async Task ARefusedAddress_IsAttachedToTheAddressAlone()
    {
        var registration = await CreateAccounts().RegisterAsync("not-an-address", _password, TestToken);

        registration.Status.ShouldBe(AccountRegistrationStatus.Rejected);
        registration.EmailRejection.ShouldNotBeNullOrEmpty();
        registration.PasswordRejection.ShouldBeNull();
    }

    [Fact]
    public async Task ARefusedPassword_IsAttachedToThePasswordAlone()
    {
        var registration = await CreateAccounts().RegisterAsync(_email, "short", TestToken);

        registration.PasswordRejection.ShouldNotBeNullOrEmpty();
        registration.EmailRejection.ShouldBeNull();
    }

    private static AppUser ExistingAccount(bool confirmed) => new()
    {
        Id = Guid.CreateVersion7(),
        UserName = _email,
        Email = _email,
        EmailConfirmed = confirmed,
    };

    private void GivenTheAddressIsTaken(bool confirmed) =>
        _store.FindByEmailAsync(_normalizedEmail, Arg.Any<CancellationToken>()).Returns(ExistingAccount(confirmed));

    private AccountsService CreateAccounts()
    {
        var options = new OptionsWrapper<IdentityOptions>(_identityOptions);

        var userManager = new UserManager<AppUser>(
            _store,
            options,
            _hasher,
            _userValidators,
            [new PasswordValidator<AppUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        var signInManager = Substitute.For<SignInManager<AppUser>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<AppUser>>(),
            options,
            NullLogger<SignInManager<AppUser>>.Instance,
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<AppUser>>());

        return new AccountsService(
            userManager,
            signInManager,
            Substitute.For<IAppUserDirectory>(),
            Substitute.For<IDateTimeProvider>());
    }

    private sealed class RefusingValidator(IdentityError error) : IUserValidator<AppUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(IdentityResult.Failed(error));
    }
}
