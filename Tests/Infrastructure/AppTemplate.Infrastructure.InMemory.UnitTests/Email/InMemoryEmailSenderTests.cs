using AppTemplate.Infrastructure.InMemory.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Email;

/// <summary>
/// The sender stands in for one that opens a socket, so what it has to get right is fidelity: the
/// message is recorded as the port received it, and stamped with the clock the test controls rather
/// than with the machine's.
/// </summary>
public sealed class InMemoryEmailSenderTests
{
    [Fact]
    public async Task SendAsync_RecordsTheMessageAsThePortReceivedIt()
    {
        using var provider = InMemoryHost.Compose();

        await InMemoryHost.SendAsync(
            provider,
            "someone@example.invalid",
            "Confirm your email address",
            "<p>https://localhost:5001/confirm-email?token=abc</p>",
            TestContext.Current.CancellationToken);

        var recorded = InMemoryHost.MailboxOf(provider).Snapshot().ShouldHaveSingleItem();
        recorded.Recipient.ShouldBe("someone@example.invalid");
        recorded.Subject.ShouldBe("Confirm your email address");
        recorded.HtmlBody.ShouldBe("<p>https://localhost:5001/confirm-email?token=abc</p>");
    }

    /// <summary>
    /// The stamp comes from the injected clock, which is what makes "this email was sent after the
    /// token expired" assertable. A sender reading <see cref="DateTimeOffset.UtcNow"/> would stamp two
    /// messages a few microseconds apart however far the test moved time.
    /// </summary>
    [Fact]
    public async Task SendAsync_StampsTheMessageWithTheClockTheTestControls()
    {
        using var provider = InMemoryHost.Compose();
        var clock = InMemoryHost.ClockOf(provider);
        var sendingInstant = new DateTimeOffset(2026, 5, 4, 9, 15, 0, TimeSpan.Zero);
        clock.Set(sendingInstant);

        await InMemoryHost.SendAsync(provider, "someone@example.invalid", "First", "<p>1</p>", TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(2));
        await InMemoryHost.SendAsync(provider, "someone@example.invalid", "Second", "<p>2</p>", TestContext.Current.CancellationToken);

        var stamps = InMemoryHost.MailboxOf(provider).Snapshot().Select(email => email.SentAt).ToList();
        stamps.ShouldBe([sendingInstant, sendingInstant.AddHours(2)]);
    }

    /// <summary>
    /// Cancellation is honoured before anything is recorded, so a cancelled request cannot leave a
    /// message behind for a later assertion to find.
    /// </summary>
    [Fact]
    public async Task SendAsync_RecordsNothingWhenTheRequestIsAlreadyCancelled()
    {
        using var provider = InMemoryHost.Compose();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => InMemoryHost.SendAsync(
                provider,
                "someone@example.invalid",
                "Subject",
                "<p>Body</p>",
                cancelled.Token));

        InMemoryHost.MailboxOf(provider).Snapshot().ShouldBeEmpty();
    }
}
