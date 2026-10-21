//#if (anyWeb)
using System.Text.RegularExpressions;

//#endif
namespace AppTemplate.EndToEnd.Tests;

internal static class Stack
{
    private const string _settingsFile = ".env.example";

    //#if (anyWeb)
    private static readonly Regex _webAppPort = new(
        "^WEB_[A-Z0-9_]+_HOST_PORT$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    //#endif
    private static readonly Lazy<Dictionary<string, string>> _defaults = new(ReadDefaults);

    internal static Uri Api => Local(Setting("API_HOST_PORT"));

    //#if (localAuth)
    internal static Uri Mailpit => Local(Setting("MAILPIT_UI_PORT"));

    //#endif
    //#if (anyWeb)
    internal static IReadOnlyList<string> WebAppPortSettings =>
        [.. _defaults.Value.Keys.Where(key => _webAppPort.IsMatch(key)).Order(StringComparer.Ordinal)];

    internal static Uri WebApp(string portSetting) => Local(Setting(portSetting));

    //#endif
    internal static HttpClient Client(Uri baseAddress) =>
        new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };

    private static string Setting(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : _defaults.Value.TryGetValue(name, out string? fallback)
                ? fallback
                : throw new InvalidOperationException($"{name} is set neither in the environment nor in {_settingsFile}.");

    private static Uri Local(string port) => new($"http://localhost:{port}/");

    private static Dictionary<string, string> ReadDefaults()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, _settingsFile);

            if (File.Exists(path))
            {
                return File.ReadLines(path)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('=', StringComparison.Ordinal))
                    .Select(line => line.Split('=', 2))
                    .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
            }
        }

        throw new InvalidOperationException(
            $"No {_settingsFile} was found above {AppContext.BaseDirectory}, so the stack's ports are unknown.");
    }
}
