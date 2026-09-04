using System.Globalization;
using System.Net;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;

/// <summary>
/// A presigned URL, read back apart. Signature Version 4 puts everything it promises into the query
/// string, so what a grant actually authorises can be asserted on directly rather than inferred from
/// the call that produced it.
/// </summary>
internal sealed class SignedUrl
{
    private readonly Dictionary<string, string> _parameters = new(StringComparer.OrdinalIgnoreCase);

    internal SignedUrl(string url)
    {
        Uri = new Uri(url, UriKind.Absolute);

        foreach (string pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);

            _parameters[WebUtility.UrlDecode(pair[..separator])] =
                WebUtility.UrlDecode(pair[(separator + 1)..]);
        }
    }

    internal Uri Uri { get; }

    /// <summary>
    /// The headers the signature covers, lower-cased by the signer. <c>host</c> is always among them
    /// and is not a header any caller sets.
    /// </summary>
    internal IReadOnlyCollection<string> SignedHeaders =>
        _parameters["X-Amz-SignedHeaders"].Split(';');

    /// <summary>How long the URL is valid for, from the instant it was signed.</summary>
    internal TimeSpan Lifetime =>
        TimeSpan.FromSeconds(int.Parse(_parameters["X-Amz-Expires"], CultureInfo.InvariantCulture));

    internal string? Parameter(string name) => _parameters.GetValueOrDefault(name);
}
