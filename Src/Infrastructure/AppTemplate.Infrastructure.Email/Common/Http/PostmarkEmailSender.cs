using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Email.Common.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AppTemplate.Infrastructure.Email.Common.Http;

/// <summary>The <see cref="IEmailSender"/> port over Postmark's HTTP API — the transport a
/// deployment picks when outbound SMTP is blocked. <b>A typed client, deliberately:</b> it inherits
/// the one outbound budget the hosts install on <c>IHttpClientFactory</c>'s defaults, which is why no
/// timeout appears here and why constructing a client by hand is forbidden under <c>Src/</c>.
/// <b>Nothing here retries:</b> the default policy replays only the safe verbs, and Postmark's send
/// endpoint takes no idempotency key, so a replay after a timeout is a second delivery the recipient
/// can see. A delivery that did not happen is the caller's to recover from, so a failure travels as
/// an exception and stops here.</summary>
internal sealed class PostmarkEmailSender(
    HttpClient httpClient,
    IOptions<PostmarkOptions> postmark,
    IOptions<EmailOptions> email,
    ILogger<PostmarkEmailSender> logger) : IEmailSender
{
    /// <summary>
    /// The header carrying the server token — the whole credential, in one value. Internal so the
    /// module's own tests can assert that <c>IHttpClientFactory</c>'s trace-level request logging
    /// still redacts it, which is a default nothing here may narrow.
    /// </summary>
    internal const string ServerTokenHeader = "X-Postmark-Server-Token";

    /// <summary>Relative to the configured base address, which is why it carries no leading slash.</summary>
    private const string _sendPath = "email";

    private static readonly JsonSerializerOptions _json = new()
    {
        // Postmark answers in PascalCase; reading case-insensitively costs nothing and survives a
        // change of theirs.
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>What is reported when the provider refused without saying anything a reader can use.</summary>
    private static readonly PostmarkRefusal _undiagnosed = new(0, "no diagnostic was returned");

    public async Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var settings = email.Value;
        var options = postmark.Value;

        // Postmark reads To as a comma-separated list, so an unquoted display name containing a comma
        // would become a second recipient. Parsing also refuses a malformed address before the call.
        var to = MailboxAddress.Parse(recipient);
        var from = new MailboxAddress(settings.FromName, settings.FromAddress);

        var payload = new OutboundMessage(
            from.ToString(),
            to.ToString(),
            subject,
            htmlBody,
            options.MessageStream);

        using var request = new HttpRequestMessage(HttpMethod.Post, _sendPath)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _json),
                Encoding.UTF8,
                "application/json"),
        };

        // On the request, not the client's default headers: the factory configures a client once, so
        // a default header would pin the token current at composition time across a rotation.
        request.Headers.TryAddWithoutValidation(ServerTokenHeader, options.ServerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await RefusalAsync(response, cancellationToken);
    }

    /// <summary>Turns the provider's answer into something an operator can act on: the status and
    /// Postmark's own error code, carrying neither the credential (what an exception message must
    /// never hold) nor the message body (the user's mail).</summary>
    private async Task<Exception> RefusalAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var refusal = await ReadRefusalAsync(response, cancellationToken);

        logger.LogError(
            "Postmark refused a message with HTTP {StatusCode} and error code {ErrorCode}: {Reason}",
            (int)response.StatusCode,
            refusal.ErrorCode,
            refusal.Message);

        return new HttpRequestException(
            $"Postmark refused the message: HTTP {(int)response.StatusCode}, error code " +
            $"{refusal.ErrorCode} ({refusal.Message}).",
            inner: null,
            response.StatusCode);
    }

    private static async Task<PostmarkRefusal> ReadRefusalAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<PostmarkRefusal>(body, _json) ?? _undiagnosed;
        }
        catch (JsonException)
        {
            // A proxy or gateway in between answers in HTML, and a failed send must not be reported
            // as a parse error here.
            return _undiagnosed;
        }
    }

    /// <summary>
    /// The members of Postmark's message payload this template uses. The names are the wire's, which
    /// is why nothing renames them: the serializer is left on its default policy on purpose.
    /// </summary>
    private sealed record OutboundMessage(
        string From,
        string To,
        string Subject,
        string HtmlBody,
        string MessageStream);

    /// <summary>Postmark's error document, which every refusal carries.</summary>
    private sealed record PostmarkRefusal(int ErrorCode, string? Message);
}
