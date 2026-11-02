using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public Uri? ResetPasswordUrl { get; set; }

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
