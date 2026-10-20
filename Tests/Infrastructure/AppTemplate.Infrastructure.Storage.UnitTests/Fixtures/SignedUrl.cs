using System.Globalization;
using System.Net;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;

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

    internal IReadOnlyCollection<string> SignedHeaders =>
        _parameters["X-Amz-SignedHeaders"].Split(';');

    internal TimeSpan Lifetime =>
        TimeSpan.FromSeconds(int.Parse(_parameters["X-Amz-Expires"], CultureInfo.InvariantCulture));

    internal string? Parameter(string name) => _parameters.GetValueOrDefault(name);
}
