using AppTemplate.Api.Features.Reminders.Composition;
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Composition;

public sealed class ExternalIdentityReminderRecipientDirectoryTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    private readonly IExternalIdentityService _identities = Substitute.For<IExternalIdentityService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMappedOwner_IsReachedByTheNameAndAddressTheProviderLastSent()
    {
        Profile(new ExternalIdentityProfile("someone@example.com", "Someone"));

        var recipient = await Directory().FindAsync(_ownerId, TestToken);

        recipient.ShouldBe(new ReminderRecipient("Someone", "someone@example.com"));
    }

    [Fact]
    public async Task AMappedOwnerWithoutAName_IsGreetedByTheirAddress()
    {
        Profile(new ExternalIdentityProfile("someone@example.com", DisplayName: null));

        var recipient = await Directory().FindAsync(_ownerId, TestToken);

        recipient.ShouldBe(new ReminderRecipient("someone@example.com", "someone@example.com"));
    }

    [Fact]
    public async Task AMappedOwnerWithoutAnAddress_HasNoRecipient()
    {
        Profile(new ExternalIdentityProfile(Email: null, "Someone"));

        (await Directory().FindAsync(_ownerId, TestToken)).ShouldBeNull();
    }

    [Fact]
    public async Task AnUnmappedOwner_HasNoRecipient()
    {
        Profile(null);

        (await Directory().FindAsync(_ownerId, TestToken)).ShouldBeNull();
    }

    private ExternalIdentityReminderRecipientDirectory Directory() => new(_identities);

    private void Profile(ExternalIdentityProfile? profile) =>
        _identities.FindProfileAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(profile);
}
