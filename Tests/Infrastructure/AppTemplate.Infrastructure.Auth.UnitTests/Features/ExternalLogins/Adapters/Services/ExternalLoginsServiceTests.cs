using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Adapters.Services;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.ExternalLogins.Adapters.Services;

public sealed class ExternalLoginsServiceTests : IDisposable
{
    private const string _provider = "google";
    private const string _subject = "108412345678901234567";

    private readonly AccountRows _rows = new();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();
    private readonly IdentityOptions _identityOptions = new()
    {
        User = { RequireUniqueEmail = true, AllowedUserNameCharacters = string.Empty },
    };
    private readonly MovableDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public void Dispose() => _rows.Dispose();

    [Fact]
    public async Task FindByExternalLoginAsync_ReturnsTheAccountThePairIsAttachedTo()
    {
        var user = _rows.Add("someone", "someone@example.com", emailConfirmed: true);
        _rows.Link(user.Id, _provider, _subject);

        var account = await CreateService().FindByExternalLoginAsync(_provider, _subject, TestToken);

        account.ShouldNotBeNull();
        account.UserId.ShouldBe(user.Id);
        account.Email.ShouldBe("someone@example.com");
    }

    [Fact]
    public async Task FindByExternalLoginAsync_ReturnsNothingWhenThePairIsAttachedToNobody()
    {
        _rows.Add("someone", "someone@example.com", emailConfirmed: true);

        (await CreateService().FindByExternalLoginAsync(_provider, _subject, TestToken)).ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindByEmailAsync_CarriesWhetherTheAddressWasEverConfirmed(bool confirmed)
    {
        _rows.Add("someone", "someone@example.com", confirmed);

        var match = await CreateService().FindByEmailAsync("someone@example.com", TestToken);

        match.ShouldNotBeNull();
        match.EmailConfirmed.ShouldBe(confirmed);
    }

    [Fact]
    public async Task FindByEmailAsync_ReturnsNothingWhenNoAccountHoldsTheAddress() =>
        (await CreateService().FindByEmailAsync("nobody@example.com", TestToken)).ShouldBeNull();

    [Fact]
    public async Task LinkAsync_AttachesThePairToAnExistingAccount()
    {
        var user = GivenTheAccountExists("someone", "someone@example.com");

        var linked = await CreateService().LinkAsync(user.Id, _provider, _subject, TestToken);

        linked.ShouldBeTrue();
        _rows.IsLinked(user.Id, _provider, _subject).ShouldBeTrue();
    }

    [Fact]
    public async Task LinkAsync_RefusesWhenTheAccountDoesNotExist()
    {
        _directory.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var linked = await CreateService().LinkAsync(Guid.CreateVersion7(), _provider, _subject, TestToken);

        linked.ShouldBeFalse();
    }

    [Fact]
    public async Task LinkAsync_RefusesWhenThePairIsAlreadyAttachedToSomebodyElse()
    {
        var other = _rows.Add("first", "first@example.com", emailConfirmed: true);
        _rows.Link(other.Id, _provider, _subject);

        var user = GivenTheAccountExists("second", "second@example.com");

        var linked = await CreateService().LinkAsync(user.Id, _provider, _subject, TestToken);

        linked.ShouldBeFalse();
        _rows.IsLinked(user.Id, _provider, _subject).ShouldBeFalse();
    }

    [Fact]
    public async Task ProvisionAsync_CreatesAnAccountAndAttachesTheProviderIdentityToIt()
    {
        var account = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        account.ShouldNotBeNull();
        account.Email.ShouldBe("someone@example.com");
        _rows.IsLinked(account.UserId, _provider, _subject).ShouldBeTrue();
    }

    [Fact]
    public async Task ProvisionAsync_StoresTheAddressAsAlreadyConfirmed()
    {
        var account = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        account.ShouldNotBeNull();
        _rows.Users.ShouldHaveSingleItem().EmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task ProvisionAsync_CreatesAnAccountWithNoPassword()
    {
        await CreateService().ProvisionAsync("someone@example.com", _provider, _subject, TestToken);

        _rows.Users.ShouldHaveSingleItem().PasswordHash.ShouldBeNull();
    }

    [Fact]
    public async Task ProvisionAsync_UsesTheAddressAsTheUserName()
    {
        var account = await CreateService().ProvisionAsync(
            "rené.dupont+news@example.com",
            _provider,
            _subject,
            TestToken);

        account.ShouldNotBeNull();
        _rows.Users.ShouldHaveSingleItem().UserName.ShouldBe("rené.dupont+news@example.com");
    }

    [Fact]
    public async Task ProvisionAsync_RefusesWhenTheAddressWasTakenInTheMeantime()
    {
        _rows.Add("first", "someone@example.com", emailConfirmed: true);

        var account = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        account.ShouldBeNull();
    }

    [Fact]
    public async Task ProvisionAsync_DeletesTheAccountItJustCreatedWhenTheLinkIsRefused()
    {
        var other = _rows.Add("first", "first@example.com", emailConfirmed: true);
        _rows.Link(other.Id, _provider, _subject);

        var account = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        account.ShouldBeNull();
        _rows.Users.ShouldHaveSingleItem().Id.ShouldBe(other.Id);
    }

    [Fact]
    public async Task ProvisionAsync_ObservesCancellationOnEntry()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateService().ProvisionAsync("someone@example.com", _provider, _subject, cancelled.Token));
    }

    private AppUser GivenTheAccountExists(string userName, string email)
    {
        var user = _rows.Add(userName, email, emailConfirmed: true);

        _directory.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        return user;
    }

    private ExternalLoginsService CreateService() =>
        new(CreateUserManager(), _directory, _clock);

    private UserManager<AppUser> CreateUserManager() =>
        new(
            _rows,
            new OptionsWrapper<IdentityOptions>(_identityOptions),
            Substitute.For<IPasswordHasher<AppUser>>(),
            [new UserValidator<AppUser>()],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);
}
