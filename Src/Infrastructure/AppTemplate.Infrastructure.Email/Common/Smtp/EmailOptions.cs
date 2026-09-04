using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AppTemplate.Infrastructure.Email.Common.Smtp;

/// <summary>
/// SMTP transport settings. Bound and validated instead of being read through four
/// null-forgiving <c>configuration["Email:…"]</c> lookups and an <c>int.Parse</c> on an absent
/// port.
/// <para>
/// Public because it is bound from configuration and its section name is part of the
/// template's contract with whoever deploys it. Everything else in this assembly is internal.
/// </para>
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

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

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add($"'{EmailOptions.SectionName}:Host' is required.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"'{EmailOptions.SectionName}:Port' must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress))
        {
            failures.Add($"'{EmailOptions.SectionName}:FromAddress' is required.");
        }
        else if (!MailboxAddress.TryParse(options.FromAddress, out _))
        {
            failures.Add($"'{EmailOptions.SectionName}:FromAddress' is not a valid email address.");
        }

        // Every mode below can end up sending in the clear. `Auto` belongs in this list because
        // MailKit resolves it to StartTlsWhenAvailable on any port other than 465 — so allowing it
        // would reopen, under a friendlier name, exactly the downgrade the other two modes are
        // rejected for. That gap is how a development compose file ended up with opportunistic TLS.
        if (IsDowngradable(options.Security) && !IsLoopback(options.Host) && !options.AllowInsecureTransport)
        {
            failures.Add(
                $"'{EmailOptions.SectionName}:Security' is '{options.Security}', which permits sending in " +
                $"plaintext, and '{EmailOptions.SectionName}:Host' is not a loopback address. Use " +
                $"'StartTls' or 'SslOnConnect', or set '{EmailOptions.SectionName}:AllowInsecureTransport' " +
                "to true to accept an unencrypted transport deliberately.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsLoopback(string host) =>
        _loopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    private static bool IsDowngradable(SecureSocketOptions security) => security is
        SecureSocketOptions.None or
        SecureSocketOptions.Auto or
        SecureSocketOptions.StartTlsWhenAvailable;
}
