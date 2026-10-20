using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

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

    private static bool IsWellFormedOrigin(string origin) =>
        origin == "*"
        || (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.Equals(origin, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
}
