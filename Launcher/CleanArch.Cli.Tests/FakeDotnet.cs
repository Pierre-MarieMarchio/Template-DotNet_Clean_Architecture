namespace CleanArch.Cli.Tests;

internal sealed class FakeDotnet : IDisposable
{
    private readonly List<string> _calls = [];
    private readonly Dictionary<string, string> _filters = new(StringComparer.Ordinal);
    private int _exit;

    public FakeDotnet() =>
        ExternalCommand.Handler = (call, _, _) =>
        {
            _calls.Add(string.Join(' ', call.Arguments));
            foreach (string filter in call.Arguments.Where(argument => argument.EndsWith(".slnf", StringComparison.Ordinal)))
            {
                _filters[filter] = File.ReadAllText(Path.Combine(call.WorkingDirectory, filter));
            }

            return _exit;
        };

    public string[] Calls => [.. _calls];

    public void ExitWith(int code) => _exit = code;

    public string FilterContentsOf(string fileName) => _filters.GetValueOrDefault(fileName, "");

    public void Dispose() => ExternalCommand.Handler = null;
}
