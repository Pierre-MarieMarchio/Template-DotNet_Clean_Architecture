using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;

public sealed class EmailChangeOptions
{
    public const string SectionName = "EmailChange";

    public Uri? ConfirmEmailChangeUrl { get; set; }

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

        if (options.TokenLifespan < _minimumLifespan || options.TokenLifespan > _maximumLifespan)
        {
            failures.Add(
                $"'{EmailChangeOptions.SectionName}:TokenLifespan' must be between {_minimumLifespan} and " +
                $"{_maximumLifespan}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
