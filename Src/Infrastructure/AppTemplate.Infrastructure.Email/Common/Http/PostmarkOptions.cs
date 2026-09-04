using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Email.Common.Http;

/// <summary>
/// Postmark's transactional API, as this module addresses it: one endpoint, one credential, one
/// message stream.
/// <para>
/// Public for the same reason <see cref="Smtp.EmailOptions"/> is: it is bound from configuration and
/// its section name is part of the contract with whoever deploys the template. The section is only
/// bound when <c>Email:Transport</c> names this transport, so a deployment that sends over SMTP owes
/// none of these keys.
/// </para>
/// </summary>
public sealed class PostmarkOptions
{
    public const string SectionName = "Postmark";

    /// <summary>
    /// A secret. It is the whole credential — anyone holding it can send mail as this domain — so it
    /// belongs in a secret store and is empty in every tracked <c>appsettings.json</c>.
    /// </summary>
    public string ServerToken { get; set; } = string.Empty;

    /// <summary>
    /// Configurable so that a local mock or an egress proxy can stand in for the real endpoint. It is
    /// where <see cref="ServerToken"/> is sent, which is why the validator refuses plaintext HTTP
    /// against anything but a loopback address.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.postmarkapp.com/";

    /// <summary>
    /// Postmark separates transactional mail from broadcast mail into streams, and bounces,
    /// suppressions and reputation are tracked per stream. <c>outbound</c> is the transactional
    /// stream every Postmark server is created with; a deployment that made its own names it here.
    /// </summary>
    public string MessageStream { get; set; } = "outbound";
}

internal sealed class PostmarkOptionsValidator : IValidateOptions<PostmarkOptions>
{
    public ValidateOptionsResult Validate(string? name, PostmarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServerToken))
        {
            failures.Add($"'{PostmarkOptions.SectionName}:ServerToken' is required.");
        }

        if (string.IsNullOrWhiteSpace(options.MessageStream))
        {
            failures.Add(
                $"'{PostmarkOptions.SectionName}:MessageStream' is required. Postmark has no implicit " +
                "stream: a send that names none is refused by the API rather than defaulted.");
        }

        failures.AddRange(ApiBaseUrlFailures(options.ApiBaseUrl));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// The counterpart of the SMTP transport rule one folder over. There the danger is a mode that
    /// falls back to plaintext; here it is a base URL that is plaintext to begin with — and what
    /// would travel in the clear is not one message but the credential that sends all of them, on
    /// every request. Loopback is the exception for the same reason, and there is no
    /// <c>AllowInsecureTransport</c> equivalent because no HTTP API refuses TLS the way a legacy
    /// relay does.
    /// </summary>
    private static IEnumerable<string> ApiBaseUrlFailures(string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            yield return
                $"'{PostmarkOptions.SectionName}:ApiBaseUrl' must be an absolute http or https URL.";

            yield break;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            yield return
                $"'{PostmarkOptions.SectionName}:ApiBaseUrl' is a plaintext http URL against a host " +
                $"that is not loopback, so '{PostmarkOptions.SectionName}:ServerToken' would be " +
                "readable on the wire on every send. Use https.";
        }
    }
}
