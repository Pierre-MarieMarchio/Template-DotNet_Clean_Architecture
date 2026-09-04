using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Options;

/// <summary>
/// Where the confirmation link points. Replaces the undocumented, null-forgiven
/// <c>configuration["AppSettings:BaseUrl"]!</c>.
/// </summary>
public sealed class EmailConfirmationOptions
{
    public const string SectionName = "EmailConfirmation";

    /// <summary>
    /// Absolute URL of the page that completes confirmation. The email address and the single-use
    /// token are appended as a URL <em>fragment</em>, which browsers never send to a server: the
    /// secret therefore stays out of access logs, out of <c>Referer</c> headers and out of any
    /// intermediary's request history. That page reads the fragment and POSTs it to the API.
    /// </summary>
    public Uri? ConfirmEmailUrl { get; set; }

    public string Subject { get; set; } = "Confirm your email address";
}

internal sealed class EmailConfirmationOptionsValidator : IValidateOptions<EmailConfirmationOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailConfirmationOptions options)
    {
        var failures = new List<string>();
        var url = options.ConfirmEmailUrl;

        if (url is null)
        {
            failures.Add($"'{EmailConfirmationOptions.SectionName}:ConfirmEmailUrl' is required.");
        }
        else
        {
            if (!url.IsAbsoluteUri)
            {
                failures.Add($"'{EmailConfirmationOptions.SectionName}:ConfirmEmailUrl' must be an absolute URL.");
            }
            else if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            {
                failures.Add(
                    $"'{EmailConfirmationOptions.SectionName}:ConfirmEmailUrl' must use the http or https scheme.");
            }

            if (url.IsAbsoluteUri && !string.IsNullOrEmpty(url.Fragment))
            {
                failures.Add(
                    $"'{EmailConfirmationOptions.SectionName}:ConfirmEmailUrl' must not carry a fragment; the " +
                    "confirmation parameters are appended as one.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Subject))
        {
            failures.Add($"'{EmailConfirmationOptions.SectionName}:Subject' must not be blank.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
