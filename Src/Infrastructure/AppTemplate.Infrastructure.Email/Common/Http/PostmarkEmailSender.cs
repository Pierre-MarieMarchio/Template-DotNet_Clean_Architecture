using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Infrastructure.Email.Common.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AppTemplate.Infrastructure.Email.Common.Http;

/// <summary>
/// The <see cref="IEmailSender"/> port over Postmark's HTTP API. The transport a deployment picks
/// when outbound SMTP is blocked, which is most of them.
/// <para>
/// <b>A typed client, deliberately.</b> The hosts install the one outbound budget on
/// <c>IHttpClientFactory</c>'s defaults from <c>Common/Outbound/</c>, and a client registered with
/// <c>AddHttpClient</c> inherits all of it — attempt timeout, total timeout, circuit breaker,
/// concurrency bound — without naming any of it. That is why no timeout appears in this file, and
/// why constructing a client by hand is forbidden under <c>Src/</c>: it would meet the factory
/// nowhere and inherit none of that. <c>NoType_ConstructsItsOwnHttpClient</c> reads this repository's
/// own text for the construction, so it cannot tell a mention from a call — which is why this
/// sentence describes the ban rather than quoting it.
/// </para>
/// <para>
/// <b>Nothing here retries, and that is the point.</b> The default policy replays only the safe
/// verbs, so this <c>POST</c> is attempted exactly once. Postmark's send endpoint takes no
/// idempotency key, so a replay after a timeout is a second delivery the recipient can see — two
/// confirmation links, two reminders — and the template already has a decision for a delivery that
/// did not happen: <c>RegisterUseCase</c> commits the account and reports
/// <c>confirmationEmailSent: false</c>, with the resend endpoint as the recovery path. A failure
/// therefore travels as an exception and stops here.
/// </para>
/// </summary>
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
        // Postmark answers in PascalCase, but a case-insensitive read costs nothing and means an
        // error document is still understood if that ever changes.
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

        // Parsed rather than passed through, exactly as the SMTP adapter does. Postmark reads To as a
        // comma-separated list, so an unquoted display name containing a comma would silently become
        // a second recipient — and parsing here also refuses a malformed address before spending a
        // request on it.
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

        // On the request rather than on the client's default headers: the factory configures a client
        // once and keeps that configuration for its lifetime, so a default header would pin whichever
        // token was current at composition time and survive a rotation. Read per send, a rotated
        // token takes effect on the next message.
        request.Headers.TryAddWithoutValidation(ServerTokenHeader, options.ServerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await RefusalAsync(response, cancellationToken);
    }

    /// <summary>
    /// Turns the provider's answer into something an operator can act on, carrying the status and
    /// Postmark's own error code — <c>300</c> is a malformed request, <c>406</c> an inactive
    /// recipient, <c>10</c> a bad token — and carrying neither the credential nor the message body.
    /// The first is what an exception message must never hold; the second is the user's mail.
    /// </summary>
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
            // Not every refusal comes from Postmark: a proxy or a gateway in between answers in HTML,
            // and a send that failed must not be reported as a parse error in this file.
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
