using CleanArch.Tooling;

namespace CleanArch.Create;

public sealed class Arguments
{
    public const string NewCommand = "new";
    public const string WebAppCommand = "web-app";
    public const string WebPackageCommand = "web-package";
    public const string PresentationCommand = "presentation";

    public static readonly IReadOnlyList<string> Commands = [NewCommand, WebAppCommand, WebPackageCommand, PresentationCommand];

    private readonly Dictionary<string, string> _templateOptions = new(StringComparer.OrdinalIgnoreCase);

    public string? Command { get; private set; }

    public string? Name { get; private set; }

    public string? Output { get; private set; }

    public bool Yes { get; private set; }

    public bool Help { get; private set; }

    public bool Setup { get; private set; }

    public string? TemplateSource { get; private set; }

    public string? Hive { get; private set; }

    public IReadOnlyDictionary<string, string> TemplateOptions => _templateOptions;

    public static Arguments Parse(IReadOnlyList<string> arguments)
    {
        var parsed = new Arguments();

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];

            if (index == 0 && !argument.StartsWith('-'))
            {
                parsed.Command = Commands.Contains(argument, StringComparer.Ordinal)
                    ? argument
                    : throw new LauncherException($"'{argument}' isn't a command. Use {string.Join(", ", Commands)}, or leave it out.");
                continue;
            }

            string key = argument;
            string? inline = null;
            int equals = argument.IndexOf('=', StringComparison.Ordinal);
            if (argument.StartsWith("--", StringComparison.Ordinal) && equals > 0)
            {
                key = argument[..equals];
                inline = argument[(equals + 1)..];
            }

            switch (key)
            {
                case "-h" or "--help" or "-?":
                    parsed.Help = true;
                    break;
                case "-y" or "--yes":
                    parsed.Yes = true;
                    break;
                case "--setup":
                    parsed.Setup = true;
                    break;
                case "-n" or "--name":
                    parsed.Name = inline ?? Value(arguments, ref index, key);
                    break;
                case "-o" or "--output":
                    parsed.Output = inline ?? Value(arguments, ref index, key);
                    break;
                case "--template-source":
                    parsed.TemplateSource = inline ?? Value(arguments, ref index, key);
                    break;
                case "--hive":
                    parsed.Hive = inline ?? Value(arguments, ref index, key);
                    break;
                case not null when key.StartsWith("--", StringComparison.Ordinal) && key.Length > 2:
                    parsed._templateOptions[key[2..]] = inline ?? OptionalValue(arguments, ref index);
                    break;
                default:
                    throw new LauncherException($"Unknown option '{argument}'. --help lists them all.");
            }
        }

        return parsed;
    }

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
