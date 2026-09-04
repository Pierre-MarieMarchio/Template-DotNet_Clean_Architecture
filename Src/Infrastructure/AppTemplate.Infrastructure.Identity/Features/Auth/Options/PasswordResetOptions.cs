using AppTemplate.Infrastructure.Identity.Common.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Options;

/// <summary>
/// Where the reset link points, and how long the token behind it stays valid.
/// <para>
/// Deliberately its own section rather than a second knob on <see cref="IdentityTokenOptions"/>: that
/// class's one lifespan is shared by every provider <c>AddDefaultTokenProviders</c> registers, and a
/// reset link has to live for far less than the one-day default a confirmation link is comfortable
/// with. <c>PasswordResetTokenProvider</c> is the named provider that keeps the two independent.
/// </para>
/// </summary>
public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>
    /// Absolute URL of the page that completes the reset. The email address and the single-use token
    /// are appended as a URL fragment, for the reason <see cref="EmailConfirmationOptions.ConfirmEmailUrl"/>
    /// gives.
    /// </summary>
    public Uri? ResetPasswordUrl { get; set; }

    /// <summary>An hour: long enough to receive and act on the mail, short enough to limit a leaked link's reach.</summary>
    public TimeSpan TokenLifespan { get; set; } = TimeSpan.FromHours(1);
}

internal sealed class PasswordResetOptionsValidator : IValidateOptions<PasswordResetOptions>
{
    private static readonly TimeSpan _minimumLifespan = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _maximumLifespan = TimeSpan.FromDays(1);

    public ValidateOptionsResult Validate(string? name, PasswordResetOptions options)
    {
        var failures = new List<string>();
        var url = options.ResetPasswordUrl;

        if (url is null)
        {
            failures.Add($"'{PasswordResetOptions.SectionName}:ResetPasswordUrl' is required.");
        }
        else
        {
            if (!url.IsAbsoluteUri)
            {
                failures.Add($"'{PasswordResetOptions.SectionName}:ResetPasswordUrl' must be an absolute URL.");
            }
            else if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            {
                failures.Add(
                    $"'{PasswordResetOptions.SectionName}:ResetPasswordUrl' must use the http or https scheme.");
            }

            if (url.IsAbsoluteUri && !string.IsNullOrEmpty(url.Fragment))
            {
                failures.Add(
                    $"'{PasswordResetOptions.SectionName}:ResetPasswordUrl' must not carry a fragment; the " +
                    "reset parameters are appended as one.");
            }
        }

        if (options.TokenLifespan < _minimumLifespan || options.TokenLifespan > _maximumLifespan)
        {
            failures.Add(
                $"'{PasswordResetOptions.SectionName}:TokenLifespan' must be between {_minimumLifespan} and " +
                $"{_maximumLifespan}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
