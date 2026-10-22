using AppTemplate.Api.Features.Auth.Composition;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Composition;

public sealed class AccountUserAccountDirectoryTests
{
    private static readonly Guid _ownerId = Guid.CreateVersion7();

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnIdTheUserStoreStillAnswers_IsAnAccount()
    {
        _accounts.FindProfileAsync(_ownerId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(
                _ownerId,
                "someone@example.com",
                EmailConfirmed: true,
                [],
                DateTimeOffset.UnixEpoch,
                TwoFactorEnabled: false,
                HasPassword: true));

        bool exists = await new AccountUserAccountDirectory(_accounts).ExistsAsync(UserId.Create(_ownerId), TestToken);

        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task AnIdTheUserStoreNoLongerAnswers_IsNotAnAccount()
    {
        _accounts.FindProfileAsync(_ownerId, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        bool exists = await new AccountUserAccountDirectory(_accounts).ExistsAsync(UserId.Create(_ownerId), TestToken);

        exists.ShouldBeFalse();
    }
}
