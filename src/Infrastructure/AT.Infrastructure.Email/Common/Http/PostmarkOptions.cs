using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Email.Common.Http;

public sealed class PostmarkOptions
{
    public const string SectionName = "Postmark";

    public string ServerToken { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.postmarkapp.com/";

    public string MessageStream { get; set; } = "outbound";
}

internal sealed class PostmarkOptionsValidator : IValidateOptions<PostmarkOptions>
{
    public ValidateOptionsResult Validate(string? name, PostmarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServerToken))
        {
            failures.Add($"'{PostmarkOptions.SectionName}:ServerToken' is required.");
        }

        if (string.IsNullOrWhiteSpace(options.MessageStream))
        {
            failures.Add(
                $"'{PostmarkOptions.SectionName}:MessageStream' is required. Postmark has no implicit " +
                "stream: a send that names none is refused by the API rather than defaulted.");
        }

        failures.AddRange(ApiBaseUrlFailures(options.ApiBaseUrl));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> ApiBaseUrlFailures(string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            yield return
                $"'{PostmarkOptions.SectionName}:ApiBaseUrl' must be an absolute http or https URL.";

            yield break;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            yield return
                $"'{PostmarkOptions.SectionName}:ApiBaseUrl' is a plaintext http URL against a host " +
                $"that is not loopback, so '{PostmarkOptions.SectionName}:ServerToken' would be " +
                "readable on the wire on every send. Use https.";
        }
    }
}
