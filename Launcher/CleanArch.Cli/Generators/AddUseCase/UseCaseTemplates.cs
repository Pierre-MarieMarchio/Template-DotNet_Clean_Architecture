namespace CleanArch.Cli.Generators.AddUseCase;

internal static class UseCaseTemplates
{
    private const string _extension = ".cs.txt";

    private static readonly string _folder = typeof(UseCaseTemplates).Namespace + ".Templates.";

    private static readonly Lazy<Dictionary<string, string>> _templates = new(Load);

    internal static string Validator => Read(nameof(Validator));

    internal static string UseCase => Read(nameof(UseCase));

    internal static string Dto => Read(nameof(Dto));

    internal static string DtoWithId => Read(nameof(DtoWithId));

    internal static string ValidatorTests => Read(nameof(ValidatorTests));

    internal static string HandlerTests => Read(nameof(HandlerTests));

    private static string Read(string name) =>
        _templates.Value.TryGetValue(name, out string? template)
            ? template
            : throw new InvalidOperationException($"No template is called {name}: {_folder}{name}{_extension} is not embedded in the tool.");

    private static Dictionary<string, string> Load()
    {
        var assembly = typeof(UseCaseTemplates).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(_folder, StringComparison.Ordinal) && resource.EndsWith(_extension, StringComparison.Ordinal))
            .ToDictionary(resource => resource[_folder.Length..^_extension.Length], resource => Text(assembly, resource), StringComparer.Ordinal);
    }

    private static string Text(System.Reflection.Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        string text = reader.ReadToEnd();

        return text.EndsWith('\n') ? text[..^1] : text;
    }
}
