using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;

namespace CleanArch.Create.Commands;

public sealed class Arguments
{
    public const string Separator = "--";

    private readonly Dictionary<string, string> _templateOptions = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private set; }

    public string? Name { get; private set; }

    public string? Output { get; private set; }

    public bool Yes { get; private set; }

    public bool Help { get; private set; }

    public bool Setup { get; private set; }

    public bool Overwrite { get; private set; }

    public string? TemplateSource { get; private set; }

    public string? Hive { get; private set; }

    public IReadOnlyDictionary<string, string> TemplateOptions => _templateOptions;

    public static Arguments Parse(IReadOnlyList<string> arguments)
    {
        var parsed = new Arguments();

        if (arguments.Count > 0 && arguments[0] == Separator)
        {
            arguments = [.. arguments.Skip(1)];
        }

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (index == 0 && !argument.StartsWith('-'))
            {
                parsed.Command = KnownCommand(argument);
            }
            else
            {
                parsed.Read(argument, arguments, ref index);
            }
        }

        return parsed;
    }

    private static string KnownCommand(string argument) =>
        LauncherCommands.Names.Contains(argument, StringComparer.Ordinal)
            ? argument
            : throw new LauncherException($"'{argument}' isn't a command. Use {string.Join(", ", LauncherCommands.Names)}, or leave it out.");

    private static (string Key, string? Inline) Split(string argument)
    {
        int equals = argument.IndexOf('=', StringComparison.Ordinal);
        return argument.StartsWith("--", StringComparison.Ordinal) && equals > 0
            ? (argument[..equals], argument[(equals + 1)..])
            : (argument, null);
    }

    private void Read(string argument, IReadOnlyList<string> arguments, ref int index)
    {
        (string key, string? inline) = Split(argument);
        switch (key)
        {
            case "-h" or "--help" or "-?":
                Help = true;
                break;
            case "-y" or "--yes":
                Yes = true;
                break;
            case "--setup":
                Setup = true;
                break;
            case "--overwrite":
                Overwrite = true;
                break;
            case "-n" or "--name":
                Name = inline ?? Value(arguments, ref index, key);
                break;
            case "-o" or "--output":
                Output = inline ?? Value(arguments, ref index, key);
                break;
            case "--template-source":
                TemplateSource = inline ?? Value(arguments, ref index, key);
                break;
            case "--hive":
                Hive = inline ?? Value(arguments, ref index, key);
                break;
            case not null when key.StartsWith("--", StringComparison.Ordinal) && key.Length > 2:
                AddTemplateOption(key[2..], inline ?? OptionalValue(arguments, ref index));
                break;
            default:
                throw new LauncherException($"Unknown option '{argument}'. --help lists them all.");
        }
    }

    private void AddTemplateOption(string name, string value) =>
        _templateOptions[name] = _templateOptions.TryGetValue(name, out string? earlier) ? earlier + Condition.ValueSeparator + value : value;

    private static string Value(IReadOnlyList<string> arguments, ref int index, string key)
    {
        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith('-'))
        {
            throw new LauncherException($"{key} needs a value.");
        }

        return arguments[++index];
    }

    private static string OptionalValue(IReadOnlyList<string> arguments, ref int index) =>
        index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? arguments[++index]
            : "true";
}
