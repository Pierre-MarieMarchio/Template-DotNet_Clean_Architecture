using AppTemplate.Infrastructure.Identity.Common.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Options;

/// <summary>
/// Where the email-change confirmation link points, and how long the token behind it stays valid.
/// <para>
/// Its own section and its own named token provider, for the same reason
/// <see cref="PasswordResetOptions"/> is not a second knob on <see cref="IdentityTokenOptions"/>:
/// sharing the "Default" provider would tie this token's lifespan to email confirmation's, which is
/// comfortable with a much longer one.
/// </para>
/// </summary>
public sealed class EmailChangeOptions
{
    public const string SectionName = "EmailChange";

    /// <summary>
    /// Absolute URL of the page that completes the change. The new address and the single-use token
    /// are appended as a URL fragment, for the reason
    /// <see cref="EmailConfirmationOptions.ConfirmEmailUrl"/> gives.
    /// </summary>
    public Uri? ConfirmEmailChangeUrl { get; set; }

    public string Subject { get; set; } = "Confirm your new email address";

    /// <summary>
    /// An hour, matching <see cref="PasswordResetOptions.TokenLifespan"/>'s reasoning: long enough to
    /// receive and act on the mail, short enough to limit a leaked link's reach.
    /// </summary>
    public TimeSpan TokenLifespan { get; set; } = TimeSpan.FromHours(1);
}

internal sealed class EmailChangeOptionsValidator : IValidateOptions<EmailChangeOptions>
{
    private static readonly TimeSpan _minimumLifespan = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _maximumLifespan = TimeSpan.FromDays(1);

    public ValidateOptionsResult Validate(string? name, EmailChangeOptions options)
    {
        var failures = new List<string>();
        var url = options.ConfirmEmailChangeUrl;

        if (url is null)
        {
            failures.Add($"'{EmailChangeOptions.SectionName}:ConfirmEmailChangeUrl' is required.");
        }
        else
        {
            if (!url.IsAbsoluteUri)
            {
                failures.Add($"'{EmailChangeOptions.SectionName}:ConfirmEmailChangeUrl' must be an absolute URL.");
            }
            else if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            {
                failures.Add(
                    $"'{EmailChangeOptions.SectionName}:ConfirmEmailChangeUrl' must use the http or https scheme.");
            }

            if (url.IsAbsoluteUri && !string.IsNullOrEmpty(url.Fragment))
            {
                failures.Add(
                    $"'{EmailChangeOptions.SectionName}:ConfirmEmailChangeUrl' must not carry a fragment; the " +
                    "confirmation parameters are appended as one.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Subject))
        {
            failures.Add($"'{EmailChangeOptions.SectionName}:Subject' must not be blank.");
        }

        if (options.TokenLifespan < _minimumLifespan || options.TokenLifespan > _maximumLifespan)
        {
            failures.Add(
                $"'{EmailChangeOptions.SectionName}:TokenLifespan' must be between {_minimumLifespan} and " +
                $"{_maximumLifespan}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
