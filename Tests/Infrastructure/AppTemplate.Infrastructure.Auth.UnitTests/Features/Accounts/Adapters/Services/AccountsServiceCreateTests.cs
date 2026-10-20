using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

/// <summary>
/// <see cref="AccountsService.CreateAsync"/> over a real <see cref="UserManager{TUser}"/> with
/// Identity's own user and password validators: which refusal is reported as what.
/// </summary>
public sealed class AccountsServiceCreateTests
{
    private const string _password = "Correct!Horse9";

    private readonly IUserEmailStore<AppUser> _store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
    private readonly IdentityOptions _identityOptions = new();
    private readonly List<IUserValidator<AppUser>> _userValidators = [new UserValidator<AppUser>()];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public AccountsServiceCreateTests()
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
    public async Task AValidAccount_IsCreated()
    {
        var creation = await CreateAccounts().CreateAsync("someone@identity.test", _password, TestToken);

        creation.Status.ShouldBe(AccountCreationStatus.Created);
    }

    [Fact]
    public async Task AnAddressAlreadyHeldAsAUserName_IsAConflict()
    {
        _store.FindByNameAsync("SOMEONE@IDENTITY.TEST", Arg.Any<CancellationToken>())
            .Returns(new AppUser { Id = Guid.CreateVersion7(), UserName = "someone@identity.test" });

        var creation = await CreateAccounts().CreateAsync("someone@identity.test", _password, TestToken);

        creation.Status.ShouldBe(AccountCreationStatus.Conflict);
    }

    /// <summary>The unique index on the address answers through the store, not a validator.</summary>
    [Fact]
    public async Task AnAddressAlreadyTaken_IsAConflict()
    {
        _store.CreateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail("someone@identity.test")));

        var creation = await CreateAccounts().CreateAsync("someone@identity.test", _password, TestToken);

        creation.Status.ShouldBe(AccountCreationStatus.Conflict);
    }

    /// <summary>
    /// Only Identity's own duplicate codes mean "address taken". A derived project's validator
    /// whose code merely contains the word must not be answered with the registration conflict,
    /// which tells the caller something about addresses it does not concern.
    /// </summary>
    [Fact]
    public async Task ACodeThatMerelyContainsDuplicate_IsARejectionNotAConflict()
    {
        _userValidators.Add(new RefusingValidator(new IdentityError
        {
            Code = "DuplicatePhoneNumber",
            Description = "That phone number is already in use.",
        }));

        var creation = await CreateAccounts().CreateAsync("someone@identity.test", _password, TestToken);

        creation.Status.ShouldBe(AccountCreationStatus.Rejected);
    }

    [Fact]
    public async Task ARefusedPassword_IsAttachedToThePasswordAlone()
    {
        var creation = await CreateAccounts().CreateAsync("someone@identity.test", "short", TestToken);

        creation.Status.ShouldBe(AccountCreationStatus.Rejected);
        creation.PasswordRejection.ShouldNotBeNullOrEmpty();
        creation.EmailRejection.ShouldBeNull();
    }

    /// <summary>Identity checks the address's format only when it also requires it to be unique.</summary>
    [Fact]
    public async Task ARefusedAddress_IsAttachedToTheAddressAlone()
    {
        _identityOptions.User.RequireUniqueEmail = true;

        var creation = await CreateAccounts().CreateAsync("not-an-address", _password, TestToken);

        creation.Status.ShouldBe(AccountCreationStatus.Rejected);
        creation.EmailRejection.ShouldNotBeNullOrEmpty();
        creation.PasswordRejection.ShouldBeNull();
    }

    /// <summary>The one value left that the store judges, so the explanation still reaches the caller.</summary>
    [Fact]
    public async Task AnUnrecognisedRefusal_IsAttachedToThePassword()
    {
        _userValidators.Add(new RefusingValidator(new IdentityError
        {
            Code = "DuplicatePhoneNumber",
            Description = "That phone number is already in use.",
        }));

        var creation = await CreateAccounts().CreateAsync("someone@identity.test", _password, TestToken);

        creation.PasswordRejection.ShouldBe("That phone number is already in use.");
    }

    private AccountsService CreateAccounts()
    {
        var options = new OptionsWrapper<IdentityOptions>(_identityOptions);

        var userManager = new UserManager<AppUser>(
            _store,
            options,
            new RecordingPasswordHasher(),
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

    /// <summary>A derived project's own user rule, refusing every account with one fixed error.</summary>
    private sealed class RefusingValidator(IdentityError error) : IUserValidator<AppUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(IdentityResult.Failed(error));
    }
}
