using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AppTemplate.Infrastructure.Email.Common.Smtp;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public const string SmtpTransport = "Smtp";

    public const string PostmarkTransport = "Postmark";

    public string Transport { get; set; } = SmtpTransport;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    public bool AllowInsecureTransport { get; set; }
}

internal sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    private static readonly string[] _loopbackHosts = ["localhost", "127.0.0.1", "::1", "[::1]"];

    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

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
