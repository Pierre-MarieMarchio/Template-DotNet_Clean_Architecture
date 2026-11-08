namespace CleanArch.Cli.Commands.Kernel;

internal sealed record ExternalCall(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, IReadOnlyDictionary<string, string>? Environment = null, string? DirectoryNote = null, bool CloseInput = false)
{
    public string Line
    {
        get
        {
            string line = string.Concat(Environment?.Select(variable => $"{variable.Key}={variable.Value} ") ?? [])
                + string.Join(' ', new[] { FileName }.Concat(Arguments.Select(Quoted)));
            return DirectoryNote is { } note ? $"{line}   (in {note})" : line;
        }
    }

    private static string Quoted(string argument) =>
        argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;
}
