using AppTemplate.Application.Features.Auth.Ports.ExternalLogins;
using AppTemplate.Infrastructure.Identity.Accounts;
using AppTemplate.Infrastructure.Identity.ExternalLogins;
using AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.ExternalLogins;

/// <summary>
/// The four primitives external sign-in is built from, over a real <see cref="UserManager{TUser}"/>
/// and its <see cref="UserValidator{TUser}"/>.
/// <para>
/// Two of these tests exist because the failures they describe are invisible until production. An
/// account provisioned without <c>EmailConfirmed</c> is created successfully and then refused at the
/// very next step, on every first sign-in; a user name derived from an address without regard for
/// <c>AllowedUserNameCharacters</c> is refused by ASP.NET Identity itself, for a reason the caller
/// cannot act on and the user cannot understand.
/// </para>
/// </summary>
public sealed class ExternalLoginsServiceTests : IDisposable
{
    private const string _provider = "google";
    private const string _subject = "108412345678901234567";

    private readonly AccountRows _rows = new();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();
    /// <summary>
    /// The deployed policy, not the framework's defaults: <c>IdentityPolicyOptionsValidator</c>
    /// refuses to let unique addresses be turned off, so a test running without that rule would be
    /// exercising a configuration no deployment has.
    /// </summary>
    private readonly IdentityOptions _identityOptions = new() { User = { RequireUniqueEmail = true } };
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
        account.UserName.ShouldBe("someone");
        account.Email.ShouldBe("someone@example.com");
    }

    [Fact]
    public async Task FindByExternalLoginAsync_ReturnsNothingWhenThePairIsAttachedToNobody()
    {
        _rows.Add("someone", "someone@example.com", emailConfirmed: true);

        (await CreateService().FindByExternalLoginAsync(_provider, _subject, TestToken)).ShouldBeNull();
    }

    /// <summary>
    /// The flag is the whole reason this operation exists: an account registered at an address
    /// nobody ever proved is exactly what must not be linked to automatically.
    /// </summary>
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
    public async Task FindByEmailAsync_ReturnsNothingWhenNoAccountHoldsTheAddress()
    {
        (await CreateService().FindByEmailAsync("nobody@example.com", TestToken)).ShouldBeNull();
    }

    [Fact]
    public async Task LinkAsync_AttachesThePairToAnExistingAccount()
    {
        var user = GivenTheAccountExists("someone", "someone@example.com");

        var status = await CreateService().LinkAsync(user.Id, _provider, _subject, TestToken);

        status.ShouldBe(ExternalLoginLinkStatus.Linked);
        _rows.IsLinked(user.Id, _provider, _subject).ShouldBeTrue();
    }

    [Fact]
    public async Task LinkAsync_RefusesWhenTheAccountDoesNotExist()
    {
        _directory.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var status = await CreateService().LinkAsync(Guid.CreateVersion7(), _provider, _subject, TestToken);

        status.ShouldBe(ExternalLoginLinkStatus.Refused);
    }

    /// <summary>
    /// The race the port names: another request attached the same pair between the caller's lookup
    /// and this call, and the unique key on the pair is what actually prevents the second one.
    /// </summary>
    [Fact]
    public async Task LinkAsync_RefusesWhenThePairIsAlreadyAttachedToSomebodyElse()
    {
        var other = _rows.Add("first", "first@example.com", emailConfirmed: true);
        _rows.Link(other.Id, _provider, _subject);

        var user = GivenTheAccountExists("second", "second@example.com");

        var status = await CreateService().LinkAsync(user.Id, _provider, _subject, TestToken);

        status.ShouldBe(ExternalLoginLinkStatus.Refused);
        _rows.IsLinked(user.Id, _provider, _subject).ShouldBeFalse();
    }

    [Fact]
    public async Task ProvisionAsync_CreatesAnAccountAndAttachesTheProviderIdentityToIt()
    {
        var outcome = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        outcome.Status.ShouldBe(ExternalAccountProvisionStatus.Provisioned);
        outcome.Account.ShouldNotBeNull();
        outcome.Account.Email.ShouldBe("someone@example.com");
        _rows.IsLinked(outcome.Account.UserId, _provider, _subject).ShouldBeTrue();
    }

    /// <summary>
    /// Without this the account is created and then refused by <c>SignInManager.CanSignInAsync</c> at
    /// the very next step of the same sign-in, because <c>Identity:RequireConfirmedEmail</c> is on —
    /// every first sign-in, in every deployment, and no unit test around the use case would see it
    /// because they all substitute this adapter away.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_StoresTheAddressAsAlreadyConfirmed()
    {
        var outcome = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        outcome.Status.ShouldBe(ExternalAccountProvisionStatus.Provisioned);
        _rows.Users.ShouldHaveSingleItem().EmailConfirmed.ShouldBeTrue();
    }

    /// <summary>
    /// There is no secret to invent and none the user could ever be told, so the account is created
    /// without one — and a stored hash would be a credential nobody chose.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_CreatesAnAccountWithNoPassword()
    {
        await CreateService().ProvisionAsync("someone@example.com", _provider, _subject, TestToken);

        _rows.Users.ShouldHaveSingleItem().PasswordHash.ShouldBeNull();
    }

    /// <summary>
    /// The trap this derivation exists for. <c>AllowedUserNameCharacters</c> is a hard gate in
    /// ASP.NET Identity's own validator, and its default set is ASCII — so an address whose local
    /// part is not would be refused by <c>CreateAsync</c> on an account the use case believes it
    /// provisioned.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_DerivesAUserNameInsideTheAllowedCharacterSet()
    {
        var outcome = await CreateService().ProvisionAsync(
            "rené.dupont+news@example.com",
            _provider,
            _subject,
            TestToken);

        outcome.Status.ShouldBe(ExternalAccountProvisionStatus.Provisioned);
        outcome.Account!.UserName.ShouldBe("ren.dupont+news");
    }

    /// <summary>
    /// An installation that narrowed the set further breaks more addresses, not fewer — which is why
    /// the allowed set is read from the options rather than assumed to be the default.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_HonoursAnInstallationThatNarrowedTheAllowedCharacterSet()
    {
        _identityOptions.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789";

        var outcome = await CreateService().ProvisionAsync(
            "some.one+news@example.com",
            _provider,
            _subject,
            TestToken);

        outcome.Status.ShouldBe(ExternalAccountProvisionStatus.Provisioned);
        outcome.Account!.UserName.ShouldBe("someonenews");
    }

    /// <summary>
    /// Nothing of the address survives the allowed set, so the name comes from an identifier
    /// instead — a refusal here would be a sign-in refused for a reason nobody could act on.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_FallsBackToAGeneratedNameWhenTheAddressYieldsNothingUsable()
    {
        var outcome = await CreateService().ProvisionAsync(
            "рене@example.com",
            _provider,
            _subject,
            TestToken);

        outcome.Status.ShouldBe(ExternalAccountProvisionStatus.Provisioned);
        outcome.Account!.UserName.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Two addresses with the same local part at different domains derive the same name. That is a
    /// collision, not a conflict, and a second candidate is what keeps it from refusing a sign-in.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_TriesAnotherNameWhenTheDerivedOneIsTaken()
    {
        _rows.Add("someone", "someone@other.example.com", emailConfirmed: true);

        var outcome = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        outcome.Status.ShouldBe(ExternalAccountProvisionStatus.Provisioned);
        outcome.Account!.UserName.ShouldNotBe("someone");
        outcome.Account.UserName.ShouldStartWith("someone");
    }

    /// <summary>
    /// A taken address is not a collision to work around: somebody claimed it between the caller's
    /// lookup and this call, and that is exactly what <c>Refused</c> describes.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_RefusesWhenTheAddressWasTakenInTheMeantime()
    {
        _rows.Add("first", "someone@example.com", emailConfirmed: true);

        var outcome = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        outcome.Status.ShouldBe(ExternalAccountProvisionStatus.Refused);
        outcome.Account.ShouldBeNull();
    }

    /// <summary>
    /// The port promises one step, and there is no transaction across the two calls. Without the
    /// compensation this leaves an account holding the address, carrying no password and attached to
    /// no provider — one nobody can ever sign into, blocking that address permanently.
    /// </summary>
    [Fact]
    public async Task ProvisionAsync_DeletesTheAccountItJustCreatedWhenTheLinkIsRefused()
    {
        var other = _rows.Add("first", "first@example.com", emailConfirmed: true);
        _rows.Link(other.Id, _provider, _subject);

        var outcome = await CreateService().ProvisionAsync(
            "someone@example.com",
            _provider,
            _subject,
            TestToken);

        outcome.Status.ShouldBe(ExternalAccountProvisionStatus.Refused);
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
        new(CreateUserManager(), _directory, new OptionsWrapper<IdentityOptions>(_identityOptions), _clock);

    /// <summary>
    /// The real manager and the real validator, so the allowed-character gate and the duplicate
    /// checks under test are ASP.NET Identity's own rather than this file's opinion of them.
    /// </summary>
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
