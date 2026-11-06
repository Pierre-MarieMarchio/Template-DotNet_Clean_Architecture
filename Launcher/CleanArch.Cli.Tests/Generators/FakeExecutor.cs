using System.Collections.Concurrent;
using System.Security.Cryptography;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Tests.Generators;

internal sealed class FakeExecutor(Func<TextEdits.Command, int> run) : IExecutor
{
    public int Run(TextEdits.Command command) => run(command);

    public (int Code, string Output) Read(TextEdits.Command command) => FakeGit.Read(command.WorkingDirectory, command.Arguments);
}

internal static class FakeGit
{
    private const string _literal = ":(literal)";

    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _commits = new(StringComparer.Ordinal);

    internal static void Commit(string root) =>
        _commits[Path.GetFullPath(root)] = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), Hash, StringComparer.Ordinal);

    internal static void Forget(string root) => _commits.TryRemove(Path.GetFullPath(root), out _);

    internal static (int Code, string Output) Read(string root, IReadOnlyList<string> arguments)
    {
        if (!_commits.TryGetValue(Path.GetFullPath(root), out var committed))
        {
            return (128, string.Empty);
        }

        string[] paths = [.. arguments.SkipWhile(argument => argument != "--").Skip(1).Select(spec => spec[_literal.Length..])];

        return (arguments[0], arguments[1]) switch
        {
            ("rev-parse", "--show-prefix") => (0, "\n"),
            ("rev-parse", _) => (0, "true\n"),
            ("ls-files", _) => (0, string.Concat(paths.Where(committed.ContainsKey).Select(path => path + "\0"))),
            ("status", _) => (0, string.Concat(paths.Select(path => Status(root, committed, path)).OfType<string>().Select(entry => entry + "\0"))),
            _ => throw new InvalidOperationException($"git {string.Join(' ', arguments)} is not one remove feature runs."),
        };
    }

    private static string? Status(string root, Dictionary<string, string> committed, string path)
    {
        string full = Path.Combine(root, path);

        if (!committed.TryGetValue(path, out string? hash))
        {
            return File.Exists(full) ? $"?? {path}" : null;
        }

        if (!File.Exists(full))
        {
            return $" D {path}";
        }

        return Hash(full) == hash ? null : $" M {path}";
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
