using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Common.Email;

/// <summary>
/// The mailbox every integration test reads its assertions out of. A defect here does not fail a
/// test — it makes one pass for the wrong reason: hand back the first message to an address instead
/// of the most recent, and a suite that resends a confirmation email goes green while confirming an
/// account with the superseded token.
/// <para>
/// Messages arrive through the port, resolved from a scope, because that is the only way anything
/// puts them there.
/// </para>
/// </summary>
public sealed class RecordedEmailsTests
{
    private const string _recipient = "someone@example.invalid";

    /// <summary>
    /// Ordering is the property. Two messages to one address is the ordinary case — a confirmation
    /// email and its resend — and the second is the one that is still valid.
    /// </summary>
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

    /// <summary>The identity store normalises addresses, so the mailbox has to match the way it does.</summary>
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

    /// <summary>
    /// The answer is per address, not "the last message sent". A suite that registers two accounts
    /// in one test would otherwise read the second account's token while asserting on the first.
    /// </summary>
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

    /// <summary>
    /// Asking for nothing is a mistake in the test, not an empty mailbox: a null return would read as
    /// "no such message" and send the author looking at the wrong end of the failure.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LastTo_RejectsAnAbsentRecipient(string? recipient)
    {
        Should.Throw<ArgumentException>(() => new RecordedEmails().LastTo(recipient!));
    }

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

    /// <summary>
    /// A snapshot is a copy. An integration test asserting on a count while other requests are still
    /// in flight would otherwise be reading a list that grows underneath it.
    /// </summary>
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
