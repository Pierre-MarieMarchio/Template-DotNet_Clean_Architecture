using AppTemplate.Api.Features.Reminders.Composition;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Composition;

public sealed class AccountReminderRecipientDirectoryTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnExistingAccount_IsReachedAndNamedByItsCurrentAddress()
    {
        _accounts.FindProfileAsync(_ownerId.Value, Arg.Any<CancellationToken>()).Returns(new UserProfile(
            _ownerId.Value,
            "someone@example.com",
            EmailConfirmed: true,
            Roles: [],
            CreatedAt: DateTimeOffset.UnixEpoch,
            TwoFactorEnabled: false,
            HasPassword: true));

        var recipient = await new AccountReminderRecipientDirectory(_accounts).FindAsync(_ownerId, TestToken);

        recipient.ShouldBe(new ReminderRecipient("someone@example.com", "someone@example.com"));
    }

    [Fact]
    public async Task AnOwnerWithNoAccount_HasNoRecipient()
    {
        _accounts.FindProfileAsync(_ownerId.Value, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var recipient = await new AccountReminderRecipientDirectory(_accounts).FindAsync(_ownerId, TestToken);

        recipient.ShouldBeNull();
    }
}
