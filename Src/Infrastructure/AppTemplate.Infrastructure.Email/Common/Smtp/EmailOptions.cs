using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AppTemplate.Infrastructure.Email.Common.Smtp;

/// <summary>
/// The module's settings: which transport carries the mail, who it is from, and — when that
/// transport is SMTP — which relay to hand it to. Bound and validated instead of being read through
/// four null-forgiving <c>configuration["Email:…"]</c> lookups and an <c>int.Parse</c> on an absent
/// port.
/// <para>
/// Public because it is bound from configuration and its section name is part of the
/// template's contract with whoever deploys it. Everything else in this assembly is internal.
/// </para>
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>MailKit over SMTP: <c>Common/Smtp/</c>.</summary>
    public const string SmtpTransport = "Smtp";

    /// <summary>Postmark's HTTP API: <c>Common/Http/</c>.</summary>
    public const string PostmarkTransport = "Postmark";

    /// <summary>
    /// Which of the two adapters behind <c>IEmailSender</c> the module composes. Defaults to
    /// <see cref="SmtpTransport"/> because that is what every deployment of this template already
    /// runs; a different default would change the behaviour of a configuration file nobody edited.
    /// A value naming neither transport stops the module from composing at all.
    /// </summary>
    public string Transport { get; set; } = SmtpTransport;

    /// <summary>Read only by <see cref="SmtpTransport"/>, and validated only for it.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    /// <summary>Optional: some relays authenticate by IP rather than credentials.</summary>
    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Defaults to mandatory STARTTLS. Any mode that can silently fall back to plaintext is
    /// rejected for a non-loopback host unless <see cref="AllowInsecureTransport"/> is set.
    /// </summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    /// <summary>
    /// Explicit opt-in to an unencrypted SMTP transport against a non-loopback host. Exists for
    /// containerised development relays such as mailpit, whose hostname is not loopback but which
    /// speak no TLS at all. It must be set deliberately: the point is that an insecure transport is
    /// a visible, auditable choice in configuration rather than something reachable by picking a
    /// permissive <see cref="Security"/> mode.
    /// </summary>
    public bool AllowInsecureTransport { get; set; }
}

internal sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    private static readonly string[] _loopbackHosts = ["localhost", "127.0.0.1", "::1", "[::1]"];

    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        // A deployment that sends over HTTP has no relay, so demanding a host, a port and a TLS mode
        // from it would be demanding values for a transport it does not use — and the only values it
        // could invent would be ones that satisfy the rules below while meaning nothing.
        //
        // The comparison is against SMTP by name rather than "anything that is not the HTTP one", so
        // an unrecognised value is reported as itself and nothing else: piling three relay failures
        // on top of a misspelt transport buries the one mistake that caused them.
        bool isSmtp = string.Equals(options.Transport, EmailOptions.SmtpTransport, StringComparison.OrdinalIgnoreCase);

        if (!isSmtp && !string.Equals(options.Transport, EmailOptions.PostmarkTransport, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"'{EmailOptions.SectionName}:Transport' is '{options.Transport}', which names no " +
                $"transport this module implements. Use '{EmailOptions.SmtpTransport}' or " +
                $"'{EmailOptions.PostmarkTransport}'.");
        }

        if (isSmtp)
        {
            failures.AddRange(SmtpFailures(options));
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress))
        {
            failures.Add($"'{EmailOptions.SectionName}:FromAddress' is required.");
        }
        else if (!MailboxAddress.TryParse(options.FromAddress, out _))
        {
            failures.Add($"'{EmailOptions.SectionName}:FromAddress' is not a valid email address.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> SmtpFailures(EmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            yield return $"'{EmailOptions.SectionName}:Host' is required.";
        }

        if (options.Port is < 1 or > 65535)
        {
            yield return $"'{EmailOptions.SectionName}:Port' must be between 1 and 65535.";
        }

        // Every mode below can end up sending in the clear. `Auto` belongs in this list because
        // MailKit resolves it to StartTlsWhenAvailable on any port other than 465 — so allowing it
        // would reopen, under a friendlier name, exactly the downgrade the other two modes are
        // rejected for. That gap is how a development compose file ended up with opportunistic TLS.
        if (IsDowngradable(options.Security) && !IsLoopback(options.Host) && !options.AllowInsecureTransport)
        {
            yield return
                $"'{EmailOptions.SectionName}:Security' is '{options.Security}', which permits sending in " +
                $"plaintext, and '{EmailOptions.SectionName}:Host' is not a loopback address. Use " +
                $"'StartTls' or 'SslOnConnect', or set '{EmailOptions.SectionName}:AllowInsecureTransport' " +
                "to true to accept an unencrypted transport deliberately.";
        }
    }

    private static bool IsLoopback(string host) =>
        _loopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    private static bool IsDowngradable(SecureSocketOptions security) => security is
        SecureSocketOptions.None or
        SecureSocketOptions.Auto or
        SecureSocketOptions.StartTlsWhenAvailable;
}
