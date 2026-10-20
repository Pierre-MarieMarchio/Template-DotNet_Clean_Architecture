using AppTemplate.Infrastructure.Email.Common.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests.Common.Smtp;

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
