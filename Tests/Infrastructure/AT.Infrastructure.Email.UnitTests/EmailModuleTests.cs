using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Email.Common.Http;
using AppTemplate.Infrastructure.Email.Common.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests;

public sealed class EmailModuleTests
{
    [Fact]
    public void AddEmailModule_ComposesTheSmtpSenderWhenNoTransportIsNamed() =>
        SenderIn(Compose(SmtpSettings())).ShouldBeOfType<MailKitEmailSender>();

    [Theory]
    [InlineData("Smtp")]
    [InlineData("smtp")]
    [InlineData("  SMTP  ")]
    public void AddEmailModule_ComposesTheSmtpSenderWhenItIsNamed(string transport)
    {
        var settings = SmtpSettings();
        settings["Email:Transport"] = transport;

        SenderIn(Compose(settings)).ShouldBeOfType<MailKitEmailSender>();
    }

    [Theory]
    [InlineData("Postmark")]
    [InlineData("postmark")]
    public void AddEmailModule_ComposesTheHttpSenderWhenItIsNamed(string transport)
    {
        var settings = PostmarkSettings();
        settings["Email:Transport"] = transport;

        SenderIn(Compose(settings)).ShouldBeOfType<PostmarkEmailSender>();
    }

    [Theory]
    [InlineData("Postmrak")]
    [InlineData("SendGrid")]
    [InlineData("Http")]
    public void AddEmailModule_RefusesToComposeATransportItDoesNotImplement(string transport)
    {
        var settings = PostmarkSettings();
        settings["Email:Transport"] = transport;

        var refusal = Should.Throw<InvalidOperationException>(() => Compose(settings));

        refusal.Message.ShouldContain(transport);
        refusal.Message.ShouldContain("Smtp");
        refusal.Message.ShouldContain("Postmark");
    }

    [Fact]
    public void AddEmailModule_StartsOnTheHttpTransportWithNoRelayConfiguredAtAll()
    {
        var provider = Compose(PostmarkSettings());

        Should.NotThrow(() => provider.GetRequiredService<IOptions<EmailOptions>>().Value);
    }

    [Fact]
    public void AddEmailModule_BindsNoProviderCredentialWhenTheTransportIsSmtp()
    {
        var provider = Compose(SmtpSettings());

        provider.GetService<IValidateOptions<PostmarkOptions>>().ShouldBeNull();
    }

    [Fact]
    public void AddEmailModule_NarrowsNothingAboutTheFactorysHeaderRedaction()
    {
        var client = HttpClientOptionsFor(Compose(PostmarkSettings()));

        client.ShouldRedactHeaderValue.ShouldNotBeNull();
        client.ShouldRedactHeaderValue(PostmarkEmailSender.ServerTokenHeader).ShouldBeTrue();
    }

    [Fact]
    public void TheRedactionRule_IsSensitive_AndSeesARedactionSetNarrowedToOtherHeaders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmailModule(new ConfigurationBuilder().AddInMemoryCollection(PostmarkSettings()).Build());
        services.AddHttpClient(nameof(PostmarkEmailSender)).RedactLoggedHeaders(["Accept"]);

        HttpClientOptionsFor(services.BuildServiceProvider())
            .ShouldRedactHeaderValue(PostmarkEmailSender.ServerTokenHeader)
            .ShouldBeFalse();
    }

    private static HttpClientFactoryOptions HttpClientOptionsFor(ServiceProvider provider) =>
        provider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(nameof(PostmarkEmailSender));

    private static IEmailSender SenderIn(ServiceProvider provider) =>
        provider.GetRequiredService<IEmailSender>();

    private static ServiceProvider Compose(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddEmailModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> SmtpSettings() =>
        new(StringComparer.Ordinal)
        {
            ["Email:Host"] = "smtp.example.invalid",
            ["Email:Port"] = "587",
            ["Email:Security"] = "StartTls",
            ["Email:FromAddress"] = "no-reply@example.invalid",
            ["Email:FromName"] = "AppTemplate",
        };

    private static Dictionary<string, string?> PostmarkSettings() =>
        new(StringComparer.Ordinal)
        {
            ["Email:Transport"] = EmailOptions.PostmarkTransport,
            ["Email:FromAddress"] = "no-reply@example.invalid",
            ["Email:FromName"] = "AppTemplate",
            ["Postmark:ServerToken"] = "postmark-server-token-3f9c1a7e",
        };
}
