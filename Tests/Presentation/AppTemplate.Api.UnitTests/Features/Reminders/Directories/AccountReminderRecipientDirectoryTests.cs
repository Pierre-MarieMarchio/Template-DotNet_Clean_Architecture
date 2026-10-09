using AppTemplate.Api.Features.Reminders.Directories;
using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Directories;

public sealed class AccountReminderRecipientDirectoryTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnExistingAccount_IsReachedByItsUserNameAndCurrentAddress()
    {
        _accounts.FindProfileAsync(_ownerId.Value, Arg.Any<CancellationToken>()).Returns(new UserProfile(
            _ownerId.Value,
            "someone",
            "someone@example.com",
            EmailConfirmed: true,
            Roles: [],
            CreatedAt: DateTimeOffset.UnixEpoch,
            TwoFactorEnabled: false));

        var recipient = await new AccountReminderRecipientDirectory(_accounts).FindAsync(_ownerId, TestToken);

        recipient.ShouldBe(new ReminderRecipient("someone", "someone@example.com"));
    }

    [Fact]
    public async Task AnOwnerWithNoAccount_HasNoRecipient()
    {
        _accounts.FindProfileAsync(_ownerId.Value, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var recipient = await new AccountReminderRecipientDirectory(_accounts).FindAsync(_ownerId, TestToken);

        recipient.ShouldBeNull();
    }
}
