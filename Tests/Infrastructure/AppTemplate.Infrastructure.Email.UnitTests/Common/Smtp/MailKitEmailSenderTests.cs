using AppTemplate.Infrastructure.Email.Common.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests.Common.Smtp;

/// <summary>
/// What the adapter refuses to send. Nothing here opens a socket, and the assertions are what proves
/// it: the configured host is an unresolvable <c>.invalid</c> name, so a send that reached
/// <c>ConnectAsync</c> would surface a socket or resolution failure. A <see cref="ParseException"/>
/// therefore says the message was rejected while being composed — before any connection was
/// attempted, and before any credential was offered to anything.
/// <para>
/// The successful path is not exercised here. It is an SMTP conversation, and the only ways to reach
/// it in a unit test are to open a real connection or to stand up a fake relay — the first is a
/// network dependency, the second proves that MailKit can talk to the fake. Delivery is covered where
/// there is a real sender to observe, through the recording sender in the integration suite.
/// </para>
/// </summary>
public sealed class MailKitEmailSenderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_RejectsABlankRecipientBeforeConnecting(string recipient)
    {
        var sender = SenderWith(Valid());

        await Should.ThrowAsync<ParseException>(
            () => sender.SendAsync(recipient, "Subject", "<p>Body</p>", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("@example.invalid")]
    [InlineData("someone@")]
    [InlineData("someone@one@two.invalid")]
    [InlineData("some one@example.invalid")]
    public async Task SendAsync_RejectsAMalformedRecipientBeforeConnecting(string recipient)
    {
        var sender = SenderWith(Valid());

        await Should.ThrowAsync<ParseException>(
            () => sender.SendAsync(recipient, "Subject", "<p>Body</p>", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_RejectsAnAbsentRecipientBeforeConnecting()
    {
        var sender = SenderWith(Valid());

        await Should.ThrowAsync<ArgumentNullException>(
            () => sender.SendAsync(null!, "Subject", "<p>Body</p>", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Defence in depth behind the options validator: a sender address that could never be a sender
    /// address stops the message here too, rather than being handed to a relay that would bounce it.
    /// </summary>
    [Fact]
    public async Task SendAsync_RefusesAFromAddressThatIsNotAnAddress()
    {
        var settings = Valid();
        settings.FromAddress = "@example.invalid";

        var sender = SenderWith(settings);

        await Should.ThrowAsync<ParseException>(
            () => sender.SendAsync(
                "someone@example.invalid",
                "Subject",
                "<p>Body</p>",
                TestContext.Current.CancellationToken));
    }

    private static MailKitEmailSender SenderWith(EmailOptions settings) =>
        new(new OptionsWrapper<EmailOptions>(settings));

    private static EmailOptions Valid() => new()
    {
        Host = "smtp.example.invalid",
        Port = 587,
        FromAddress = "no-reply@example.invalid",
        FromName = "AppTemplate",
        Security = SecureSocketOptions.StartTls,
    };
}
