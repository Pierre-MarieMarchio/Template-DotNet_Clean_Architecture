using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;

public sealed class EmailConfirmationOptions
{
    public const string SectionName = "EmailConfirmation";

    public Uri? ConfirmEmailUrl { get; set; }
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

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
