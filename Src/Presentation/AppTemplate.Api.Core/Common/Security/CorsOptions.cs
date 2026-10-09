using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Core.Common.Security;

/// <summary>
/// Which browser origins may call this API cross-origin.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class CorsOptions
{
    /// <summary>The configuration section this type is bound from.</summary>
    public const string SectionName = "Cors";

    /// <summary>
    /// Exact origins, such as <c>https://app.example</c>. Empty by default, which allows no
    /// cross-origin caller; same-origin callers are unaffected either way.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];
}

internal sealed class CorsOptionsValidator : IValidateOptions<CorsOptions>
{
    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        foreach (string origin in options.AllowedOrigins)
        {
            if (!IsWellFormedOrigin(origin))
            {
                failures.Add(
                    $"'{CorsOptions.SectionName}:{nameof(CorsOptions.AllowedOrigins)}' contains " +
                    $"'{origin}', which is not an origin. Write scheme and host, with an optional port " +
                    "and nothing after it: 'https://app.example'.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // A trailing slash, a path or a missing scheme is registered as written and then matches no
    // browser request, since a browser sends the origin alone. "*" is the framework's own way to
    // say any origin and is left to the deployer.
    private static bool IsWellFormedOrigin(string origin) =>
        origin == "*"
        || (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
}
