using System.Text;

namespace CleanArch.Cli.Generators.Shared;

internal static class SourceText
{
    private static readonly string[] _generatedFolders = ["obj", "bin", "node_modules"];

    internal static string Decode(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes, bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0, bytes.Length - (bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0));

    internal static IEnumerable<string> SourceFiles(string folder, string pattern) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
                .Where(file => !_generatedFolders.Any(generated => file.Contains($"{Path.DirectorySeparatorChar}{generated}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            : [];

    internal static string Sorted(string source)
    {
        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        int usings = lines.TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal)).Count();

        string[] sorted = [.. lines[..usings].Distinct(StringComparer.Ordinal).OrderBy(UsingKey, StringComparer.Ordinal)];

        return string.Join('\n', [.. sorted, .. lines[usings..]]).TrimEnd('\n') + "\n";
    }

    internal static string UsingKey(string line) => (line.StartsWith("using System", StringComparison.Ordinal) ? "0" : "1") + line.TrimEnd(';').ToUpperInvariant();
}
