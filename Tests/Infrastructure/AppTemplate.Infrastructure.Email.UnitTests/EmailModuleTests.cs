using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Email.Common.Http;
using AppTemplate.Infrastructure.Email.Common.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests;

/// <summary>
/// Which transport the module composes, and what it does with a key it does not recognise.
/// <para>
/// This is the half of the decision no options validator can hold: the transport decides what is
/// registered, so it is read before any provider exists to validate options from. A test that only
/// exercised the validator would leave the composition itself unchecked — and the composition is
/// where a silent fallback would live.
/// </para>
/// </summary>
public sealed class EmailModuleTests
{
    /// <summary>
    /// The value every existing deployment of this template has, because none of them has the key at
    /// all. A different default here is not a new feature, it is every cloned repository sending its
    /// mail somewhere else after an upgrade.
    /// </summary>
    [Fact]
    public void AddEmailModule_ComposesTheSmtpSenderWhenNoTransportIsNamed()
    {
        SenderIn(Compose(SmtpSettings())).ShouldBeOfType<MailKitEmailSender>();
    }

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

    /// <summary>
    /// A typo in one configuration key must not decide where a deployment's mail goes. Falling back
    /// to SMTP would be the friendly behaviour and the wrong one: the process would start, the
    /// operator would see nothing, and the transport they configured would never be used.
    /// </summary>
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

    /// <summary>
    /// The SMTP transport's relay settings are not the HTTP transport's business. A deployment that
    /// sends over HTTP has no host, no port and no TLS mode, and demanding them would only get values
    /// invented to satisfy a validator.
    /// </summary>
    [Fact]
    public void AddEmailModule_StartsOnTheHttpTransportWithNoRelayConfiguredAtAll()
    {
        var provider = Compose(PostmarkSettings());

        Should.NotThrow(() => provider.GetRequiredService<IOptions<EmailOptions>>().Value);
    }

    /// <summary>
    /// And the reverse, which is the half that could regress silently: an SMTP deployment must not be
    /// asked for an API credential it has no reason to hold.
    /// </summary>
    [Fact]
    public void AddEmailModule_BindsNoProviderCredentialWhenTheTransportIsSmtp()
    {
        var provider = Compose(SmtpSettings());

        provider.GetService<IValidateOptions<PostmarkOptions>>().ShouldBeNull();
    }

    /// <summary>
    /// <c>IHttpClientFactory</c>'s logging handler writes every request header at trace level, and
    /// redacts every value unless it is handed a list of the ones to redact — at which point it
    /// redacts <em>only</em> those. So the way this credential reaches a log aggregator is not a
    /// missing call but an added one: a <c>RedactLoggedHeaders</c> on this client that names some
    /// other header replaces the strict default with a permissive set. This test is what fails when
    /// somebody adds it.
    /// </summary>
    [Fact]
    public void AddEmailModule_NarrowsNothingAboutTheFactorysHeaderRedaction()
    {
        var client = HttpClientOptionsFor(Compose(PostmarkSettings()));

        client.ShouldRedactHeaderValue.ShouldNotBeNull();
        client.ShouldRedactHeaderValue(PostmarkEmailSender.ServerTokenHeader).ShouldBeTrue();
    }

    /// <summary>
    /// Proves the assertion above can fail, by composing the mistake it exists to catch. Without this,
    /// a test asserting a framework default would report "the token is redacted" whatever the module
    /// did, and would go on reporting it after a release that changed the default.
    /// </summary>
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

    /// <summary>
    /// The module alone, with only what a host puts in the container before composing it. The reminder
    /// notifier's own dependencies live in other modules, so it is the sender that is resolved here —
    /// which is what these tests are about.
    /// </summary>
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

    /// <summary>Deliberately carries no <c>Email:Host</c>, <c>Email:Port</c> or <c>Email:Security</c>.</summary>
    private static Dictionary<string, string?> PostmarkSettings() =>
        new(StringComparer.Ordinal)
        {
            ["Email:Transport"] = EmailOptions.PostmarkTransport,
            ["Email:FromAddress"] = "no-reply@example.invalid",
            ["Email:FromName"] = "AppTemplate",
            ["Postmark:ServerToken"] = "postmark-server-token-3f9c1a7e",
        };
}
