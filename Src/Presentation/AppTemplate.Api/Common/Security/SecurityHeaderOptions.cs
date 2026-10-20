using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

public sealed class SecurityHeaderOptions
{
    public const string SectionName = "SecurityHeaders";

    public const string DefaultContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public string ContentSecurityPolicy { get; set; } = DefaultContentSecurityPolicy;
}

internal sealed class SecurityHeaderOptionsValidator : IValidateOptions<SecurityHeaderOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityHeaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ContentSecurityPolicy))
        {
            failures.Add(
                $"'{SecurityHeaderOptions.SectionName}:{nameof(SecurityHeaderOptions.ContentSecurityPolicy)}' " +
                "must not be blank. A blank policy sends an empty header, which browsers treat as " +
                "'deny everything' on some directives and 'no policy' on others.");
        }
        else
        {
            if (options.ContentSecurityPolicy.Any(char.IsControl))
            {
                failures.Add(
                    $"'{SecurityHeaderOptions.SectionName}:{nameof(SecurityHeaderOptions.ContentSecurityPolicy)}' " +
                    "contains a control character.");
            }

            if (!options.ContentSecurityPolicy.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"'{SecurityHeaderOptions.SectionName}:{nameof(SecurityHeaderOptions.ContentSecurityPolicy)}' " +
                    "declares no 'frame-ancestors' directive.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
