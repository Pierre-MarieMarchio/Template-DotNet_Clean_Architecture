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

/// <summary>
/// <see cref="EmailTokensService.ConfirmEmailAsync"/> over a real <see cref="UserManager{TUser}"/>, standing in
/// for the "Default" provider with a fake that mimics the one property of
/// <c>DataProtectorTokenProvider</c> this suite cares about: a token embeds the security stamp at
/// generation time and is rejected once the stamp no longer matches.
/// </summary>
public sealed class EmailTokensServiceConfirmationTests
{
    private const string _knownEmail = "someone@example.test";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Proves the single-use claim <c>ConfirmEmailCommand</c> documents. Before
    /// <c>ConfirmEmailAsync</c> rotated the security stamp on success, the stub's <c>ValidateAsync</c>
    /// kept accepting the first token forever, and this test failed with <c>true</c> instead of
    /// <c>false</c> on the replay.
    /// </summary>
    [Fact]
    public async Task ARedeemedToken_CannotBeReplayed()
    {
        var (store, user) = GivenAnUnconfirmedAccount();
        var tokens = CreateTokens(store, out var userManager);

        string token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        var first = await tokens.ConfirmEmailAsync(_knownEmail, token, TestToken);
        first.ShouldBeTrue();

        var replay = await tokens.ConfirmEmailAsync(_knownEmail, token, TestToken);
        replay.ShouldBeFalse();
    }

    [Fact]
    public async Task ARedeemedToken_RotatesTheSecurityStamp()
    {
        var (store, user) = GivenAnUnconfirmedAccount();
        var tokens = CreateTokens(store, out var userManager);

        string stampBeforeConfirmation = user.SecurityStamp!;
        string token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        await tokens.ConfirmEmailAsync(_knownEmail, token, TestToken);

        user.SecurityStamp.ShouldNotBe(stampBeforeConfirmation);
    }

    [Fact]
    public async Task AWrongToken_IsRefusedAndRotatesNothing()
    {
        var (store, user) = GivenAnUnconfirmedAccount();
        var tokens = CreateTokens(store, out _);
        string stampBeforeConfirmation = user.SecurityStamp!;

        var confirmed = await tokens.ConfirmEmailAsync(_knownEmail, "not-the-real-token", TestToken);

        confirmed.ShouldBeFalse();
        user.SecurityStamp.ShouldBe(stampBeforeConfirmation);
    }

    /// <summary>
    /// Refused exactly as a wrong token is: the port no longer tells an unknown address apart.
    /// </summary>
    [Fact]
    public async Task AnUnknownAddress_IsRefusedLikeAWrongToken()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserSecurityStampStore<AppUser>>();
        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var confirmed = await CreateTokens(store, out _).ConfirmEmailAsync(_knownEmail, "any-token", TestToken);

        confirmed.ShouldBeFalse();
    }

    private static (IUserEmailStore<AppUser> Store, AppUser User) GivenAnUnconfirmedAccount()
    {
        var store = Substitute.For<IUserEmailStore<AppUser>, IUserSecurityStampStore<AppUser>>();
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "someone",
            SecurityStamp = Guid.CreateVersion7().ToString("N"),
        };

        store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        var securityStampStore = (IUserSecurityStampStore<AppUser>)store;
        securityStampStore.GetSecurityStampAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<string?>(((AppUser)callInfo[0]!).SecurityStamp));
        securityStampStore.SetSecurityStampAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                ((AppUser)callInfo[0]!).SecurityStamp = (string)callInfo[1]!;
                return Task.CompletedTask;
            });

        return (store, user);
    }

    private static EmailTokensService CreateTokens(
        IUserEmailStore<AppUser> store,
        out UserManager<AppUser> userManager)
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());

        userManager = new UserManager<AppUser>(
            store,
            options,
            Substitute.For<IPasswordHasher<AppUser>>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        // Stands in for the "Default" DataProtectorTokenProvider that ASP.NET Identity's own
        // AddDefaultTokenProviders registers and email confirmation resolves to: a token is only as
        // good as the security stamp it was minted against.
        userManager.RegisterTokenProvider("Default", new StampBoundTokenProvider());

        return new EmailTokensService(userManager, Substitute.For<IAppUserDirectory>());
    }

    /// <summary>
    /// A minimal stand-in for <c>DataProtectorTokenProvider</c>'s one relevant property: the token
    /// it hands out is only valid against the security stamp the user carried at the moment it was
    /// generated.
    /// </summary>
    private sealed class StampBoundTokenProvider : IUserTwoFactorTokenProvider<AppUser>
    {
        public async Task<string> GenerateAsync(string purpose, UserManager<AppUser> manager, AppUser user) =>
            await manager.GetSecurityStampAsync(user) ?? string.Empty;

        public async Task<bool> ValidateAsync(string purpose, string token, UserManager<AppUser> manager, AppUser user) =>
            token == await manager.GetSecurityStampAsync(user);

        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(true);
    }
}
