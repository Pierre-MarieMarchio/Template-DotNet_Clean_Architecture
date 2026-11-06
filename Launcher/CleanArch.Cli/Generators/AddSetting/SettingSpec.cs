
namespace CleanArch.Cli.Generators.AddSetting;

internal sealed record SettingSpec(string Prefix, string Features, string Section, IReadOnlyList<SettingKey> Keys, string? Manifest, bool TakesConfiguration)
{
    internal string Options => Section + "Options";

    internal string Validator => Section + "OptionsValidator";

    internal string Namespace => $"{Prefix}.Application.Features.{Features}.Configuration.Options";

    internal string Variable(SettingKey key) => $"{UpperSnake(Section)}_{UpperSnake(key.Name)}";

    private static string UpperSnake(string pascal) =>
        string.Concat(pascal.Select((character, index) => char.IsUpper(character) && index > 0 ? "_" + character : char.ToUpperInvariant(character).ToString()));
}
