using System.Net;
using System.Text;
using System.Text.Json;
using AppTemplate.Infrastructure.Email.Common.Http;
using AppTemplate.Infrastructure.Email.Common.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests.Common.Http;

public sealed class PostmarkEmailSenderTests : IDisposable
{
    private const string _serverToken = "postmark-server-token-3f9c1a7e-do-not-log-me";

    private readonly RecordingPostmarkEndpoint _endpoint = new();
    private readonly RecordingLogger _logger = new();
    private readonly HttpClient _httpClient;

    public PostmarkEmailSenderTests()
    {
        _httpClient = new HttpClient(_endpoint, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.postmark.invalid/"),
        };
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _httpClient.Dispose();
        _endpoint.Dispose();
    }

    [Fact]
    public async Task SendAsync_PostsToTheProvidersSendEndpoint()
    {
        await SendAsync();

        _endpoint.Method.ShouldBe(HttpMethod.Post);
        _endpoint.RequestUri.ShouldBe(new Uri("https://api.postmark.invalid/email"));
    }

    [Fact]
    public async Task SendAsync_CarriesTheMessageInTheMembersTheProviderReads()
    {
        await SendAsync(recipient: "ada@example.invalid", subject: "Confirm your email", htmlBody: "<p>Hello</p>");

        var body = Sent();

        body.GetProperty("From").GetString().ShouldBe("\"AppTemplate\" <no-reply@example.invalid>");
        body.GetProperty("To").GetString().ShouldBe("ada@example.invalid");
        body.GetProperty("Subject").GetString().ShouldBe("Confirm your email");
        body.GetProperty("HtmlBody").GetString().ShouldBe("<p>Hello</p>");
        body.GetProperty("MessageStream").GetString().ShouldBe("outbound");
    }

    [Fact]
    public async Task SendAsync_TakesTheSenderIdentityFromTheModulesOwnSettings()
    {
        var email = ValidEmail();
        email.FromAddress = "alerts@example.invalid";
        email.FromName = "AppTemplate Alerts";

        await SendAsync(email: email);

        Sent().GetProperty("From").GetString().ShouldBe("\"AppTemplate Alerts\" <alerts@example.invalid>");
    }

    [Fact]
    public async Task SendAsync_QuotesADisplayNameThatWouldOtherwiseReadAsTwoRecipients()
    {
        await SendAsync(recipient: "Lovelace, Ada <ada@example.invalid>");

        string to = Sent().GetProperty("To").GetString().ShouldNotBeNull();

        to.ShouldBe("\"Lovelace, Ada\" <ada@example.invalid>");
        MailboxAddress.Parse(to).Address.ShouldBe("ada@example.invalid");
    }

    [Fact]
    public async Task SendAsync_AuthenticatesWithTheServerTokenHeader()
    {
        await SendAsync();

        _endpoint.ServerTokens.ShouldHaveSingleItem().ShouldBe(_serverToken);
    }

    [Fact]
    public async Task SendAsync_ReadsTheServerTokenAgainForEveryMessage()
    {
        var postmark = ValidPostmark();
        var sender = SenderWith(ValidEmail(), new OptionsWrapper<PostmarkOptions>(postmark));

        await sender.SendAsync("ada@example.invalid", "One", "<p>One</p>", TestToken);

        postmark.ServerToken = "postmark-server-token-rotated";
        await sender.SendAsync("ada@example.invalid", "Two", "<p>Two</p>", TestToken);

        _endpoint.RequestCount.ShouldBe(2);
        _endpoint.ServerTokens.ShouldHaveSingleItem().ShouldBe("postmark-server-token-rotated");
    }

    [Fact]
    public async Task SendAsync_ReportsTheProvidersOwnDiagnosticWhenAMessageIsRefused()
    {
        _endpoint.Answers(
            HttpStatusCode.UnprocessableEntity,
            """{"ErrorCode":406,"Message":"You tried to send to a recipient that has been marked as inactive."}""");

        var refusal = await Should.ThrowAsync<HttpRequestException>(() => SendAsync());

        refusal.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refusal.Message.ShouldContain("406");
        refusal.Message.ShouldContain("marked as inactive");
    }

    [Theory]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("")]
    public async Task SendAsync_StillReportsARefusalThatCarriesNoProviderDocument(string body)
    {
        _endpoint.Answers(HttpStatusCode.BadGateway, body);

        var refusal = await Should.ThrowAsync<HttpRequestException>(() => SendAsync());

        refusal.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        refusal.Message.ShouldContain("502");
    }

    [Fact]
    public async Task SendAsync_AttemptsARefusedSendExactlyOnce()
    {
        _endpoint.Answers(HttpStatusCode.ServiceUnavailable, """{"ErrorCode":0,"Message":"Service Unavailable"}""");

        await Should.ThrowAsync<HttpRequestException>(() => SendAsync());

        _endpoint.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_LeaksTheServerTokenIntoNeitherTheLogNorTheException()
    {
        _endpoint.Answers(
            HttpStatusCode.Unauthorized,
            """{"ErrorCode":10,"Message":"Your request did not submit the correct API token."}""");

        var refusal = await Should.ThrowAsync<HttpRequestException>(() => SendAsync());

        refusal.ToString().ShouldNotContain(_serverToken);
        _logger.Records.ShouldNotBeEmpty("The refusal has to be logged, or this assertion holds vacuously.");
        _logger.Records.ShouldAllBe(record => !record.Contains(_serverToken, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@example.invalid")]
    [InlineData("someone@one@two.invalid")]
    public async Task SendAsync_RejectsAMalformedRecipientWithoutSpendingARequest(string recipient)
    {
        await Should.ThrowAsync<ParseException>(() => SendAsync(recipient: recipient));

        _endpoint.RequestCount.ShouldBe(0);
    }

    private Task SendAsync(
        string recipient = "ada@example.invalid",
        string subject = "Subject",
        string htmlBody = "<p>Body</p>",
        EmailOptions? email = null) =>
        SenderWith(email ?? ValidEmail(), new OptionsWrapper<PostmarkOptions>(ValidPostmark()))
            .SendAsync(recipient, subject, htmlBody, TestToken);

    private PostmarkEmailSender SenderWith(EmailOptions email, IOptions<PostmarkOptions> postmark) =>
        new(_httpClient, postmark, new OptionsWrapper<EmailOptions>(email), _logger);

    private JsonElement Sent()
    {
        _endpoint.Body.ShouldNotBeNull();

        return JsonDocument.Parse(_endpoint.Body).RootElement.Clone();
    }

    private static EmailOptions ValidEmail() => new()
    {
        Transport = EmailOptions.PostmarkTransport,
        FromAddress = "no-reply@example.invalid",
        FromName = "AppTemplate",
    };

    private static PostmarkOptions ValidPostmark() => new() { ServerToken = _serverToken };
}

internal sealed class RecordingLogger : ILogger<PostmarkEmailSender>
{
    private readonly List<string> _records = [];

    internal IReadOnlyList<string> Records => _records;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _records.Add($"{formatter(state, exception)} {exception}");
    }
}

internal sealed class RecordingPostmarkEndpoint : HttpMessageHandler
{
    private Func<HttpResponseMessage> _answer = Accepted;

    internal int RequestCount { get; private set; }

    internal HttpMethod? Method { get; private set; }

    internal Uri? RequestUri { get; private set; }

    internal string? Body { get; private set; }

    internal IReadOnlyList<string> ServerTokens { get; private set; } = [];

    internal void Answers(HttpStatusCode status, string body) =>
        _answer = () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        RequestCount++;
        Method = request.Method;
        RequestUri = request.RequestUri;

        Body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        ServerTokens = request.Headers.TryGetValues(PostmarkEmailSender.ServerTokenHeader, out var values)
            ? [.. values]
            : [];

        return _answer();
    }

    private static HttpResponseMessage Accepted() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"ErrorCode":0,"Message":"OK","MessageID":"0a129aee-e1cd-480d-b08d-4f48548ff48d"}""",
                Encoding.UTF8,
                "application/json"),
        };
}
