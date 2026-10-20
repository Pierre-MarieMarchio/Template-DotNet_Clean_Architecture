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

internal sealed class PostmarkEmailSender(
    HttpClient httpClient,
    IOptions<PostmarkOptions> postmark,
    IOptions<EmailOptions> email,
    ILogger<PostmarkEmailSender> logger) : IEmailSender
{
    internal const string ServerTokenHeader = "X-Postmark-Server-Token";

    private const string _sendPath = "email";

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly PostmarkRefusal _undiagnosed = new(0, "no diagnostic was returned");

    public async Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var settings = email.Value;
        var options = postmark.Value;

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

        request.Headers.TryAddWithoutValidation(ServerTokenHeader, options.ServerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await RefusalAsync(response, cancellationToken);
    }

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
            return _undiagnosed;
        }
    }

    private sealed record OutboundMessage(
        string From,
        string To,
        string Subject,
        string HtmlBody,
        string MessageStream);

    private sealed record PostmarkRefusal(int ErrorCode, string? Message);
}
