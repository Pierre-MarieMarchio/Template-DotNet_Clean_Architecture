using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// The one response-security header whose correct value depends on what the deployment serves.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
/// <remarks>
/// Only the policy is configurable. <c>X-Content-Type-Options</c>, <c>Referrer-Policy</c> and
/// <c>X-Frame-Options</c> have exactly one correct value for a JSON API, and a knob on those is a
/// knob for turning a defence off. The policy is different: a fork that starts serving HTML from
/// this origin needs a different one, and it is the only header here whose wrong value breaks a page
/// rather than merely loosening it.
/// </remarks>
public sealed class SecurityHeaderOptions
{
    public const string SectionName = "SecurityHeaders";

    /// <summary>
    /// Default-deny, which is what a JSON API needs: no document served from this origin may load a
    /// script, a style, an image or a font, nobody may frame it, and no injected <c>&lt;base&gt;</c>
    /// can repoint its relative URLs.
    /// </summary>
    public const string DefaultContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>
    /// Sent on every response the API itself produces. The API-reference page in Development is the
    /// one exception and is not governed by this value — see <see cref="SecurityHeadersExtensions"/>.
    /// </summary>
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
            // A configured value reaches a response header verbatim, so a control character in it
            // would be header injection with the deployment's own configuration as the vector.
            if (options.ContentSecurityPolicy.Any(char.IsControl))
            {
                failures.Add(
                    $"'{SecurityHeaderOptions.SectionName}:{nameof(SecurityHeaderOptions.ContentSecurityPolicy)}' " +
                    "contains a control character.");
            }

            // Losing this directive silently drops half of the clickjacking control: X-Frame-Options
            // is the legacy half, and modern browsers that see a policy prefer frame-ancestors.
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
