using Microsoft.Extensions.Options;

namespace AppTemplate.FrameworkSite.Common.Hosting;

internal sealed class SignInOptionsValidator : IValidateOptions<SignInOptions>
{
    public ValidateOptionsResult Validate(string? name, SignInOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        const string section = SignInOptions.SectionName;

        if (options.Authority is not { IsAbsoluteUri: true, Scheme: "http" or "https" } authority)
        {
            failures.Add($"'{section}:{nameof(options.Authority)}' is required: the provider's absolute issuer URL.");
        }
        else if (options.RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add(
                $"'{section}:{nameof(options.Authority)}' is not https, which " +
                $"'{section}:{nameof(options.RequireHttpsMetadata)}' requires: the tokens are redeemed there.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add($"'{section}:{nameof(options.ClientId)}' is required: the public client the provider registered for this app.");
        }

        if (!Scopes(options).Contains("openid", StringComparer.Ordinal))
        {
            failures.Add($"'{section}:{nameof(options.Scope)}' must include 'openid': the app reads who signed in from the identity token.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static string[] Scopes(SignInOptions options) =>
        (options.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
