using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Common.Email;

public sealed class RecordedEmailsTests
{
    private const string _recipient = "someone@example.invalid";

    [Fact]
    public async Task LastTo_ReturnsTheMostRecentMessageRatherThanTheFirst()
    {
        using var provider = InMemoryHost.Compose();

        await InMemoryHost.SendAsync(provider, _recipient, "First", "<p>1</p>", TestContext.Current.CancellationToken);
        await InMemoryHost.SendAsync(provider, _recipient, "Second", "<p>2</p>", TestContext.Current.CancellationToken);
        await InMemoryHost.SendAsync(provider, _recipient, "Third", "<p>3</p>", TestContext.Current.CancellationToken);

        var found = InMemoryHost.MailboxOf(provider).LastTo(_recipient);

        found.ShouldNotBeNull();
        found.Subject.ShouldBe("Third");
        found.HtmlBody.ShouldBe("<p>3</p>");
    }

    [Fact]
    public async Task LastTo_MatchesTheAddressWhateverItsCasing()
    {
        using var provider = InMemoryHost.Compose();

        await InMemoryHost.SendAsync(
            provider,
            "Someone@Example.Invalid",
            "Confirm your email address",
            "<p>Body</p>",
            TestContext.Current.CancellationToken);

        var found = InMemoryHost.MailboxOf(provider).LastTo("someone@example.invalid");

        found.ShouldNotBeNull();
        found.Subject.ShouldBe("Confirm your email address");
    }

    [Fact]
    public async Task LastTo_AnswersForTheAddressAskedAboutAndNotTheLatestMessage()
    {
        using var provider = InMemoryHost.Compose();

        await InMemoryHost.SendAsync(provider, "first@example.invalid", "To first", "<p>1</p>", TestContext.Current.CancellationToken);
        await InMemoryHost.SendAsync(provider, "second@example.invalid", "To second", "<p>2</p>", TestContext.Current.CancellationToken);
        await InMemoryHost.SendAsync(provider, "first@example.invalid", "To first again", "<p>3</p>", TestContext.Current.CancellationToken);

        var mailbox = InMemoryHost.MailboxOf(provider);

        mailbox.LastTo("first@example.invalid")!.Subject.ShouldBe("To first again");
        mailbox.LastTo("second@example.invalid")!.Subject.ShouldBe("To second");
    }

    [Fact]
    public async Task LastTo_ReturnsNothingWhenNoMessageWentToThatAddress()
    {
        using var provider = InMemoryHost.Compose();

        await InMemoryHost.SendAsync(provider, _recipient, "Subject", "<p>Body</p>", TestContext.Current.CancellationToken);

        InMemoryHost.MailboxOf(provider).LastTo("nobody@example.invalid").ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LastTo_RejectsAnAbsentRecipient(string? recipient) =>
        Should.Throw<ArgumentException>(() => new RecordedEmails().LastTo(recipient!));

    [Fact]
    public async Task Snapshot_ListsEveryMessageOldestFirst()
    {
        using var provider = InMemoryHost.Compose();

        await InMemoryHost.SendAsync(provider, _recipient, "First", "<p>1</p>", TestContext.Current.CancellationToken);
        await InMemoryHost.SendAsync(provider, _recipient, "Second", "<p>2</p>", TestContext.Current.CancellationToken);

        InMemoryHost.MailboxOf(provider)
            .Snapshot()
            .Select(email => email.Subject)
            .ShouldBe(["First", "Second"]);
    }

    [Fact]
    public async Task Snapshot_DoesNotChangeWhenMoreMailArrives()
    {
        using var provider = InMemoryHost.Compose();
        await InMemoryHost.SendAsync(provider, _recipient, "First", "<p>1</p>", TestContext.Current.CancellationToken);

        var taken = InMemoryHost.MailboxOf(provider).Snapshot();
        await InMemoryHost.SendAsync(provider, _recipient, "Second", "<p>2</p>", TestContext.Current.CancellationToken);

        taken.Count.ShouldBe(1);
        InMemoryHost.MailboxOf(provider).Snapshot().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Clear_EmptiesTheMailboxSoTheNextTestCannotReadThisOnesMail()
    {
        using var provider = InMemoryHost.Compose();
        await InMemoryHost.SendAsync(provider, _recipient, "Subject", "<p>Body</p>", TestContext.Current.CancellationToken);
        var mailbox = InMemoryHost.MailboxOf(provider);

        mailbox.Clear();

        mailbox.Snapshot().ShouldBeEmpty();
        mailbox.LastTo(_recipient).ShouldBeNull();
    }
}
