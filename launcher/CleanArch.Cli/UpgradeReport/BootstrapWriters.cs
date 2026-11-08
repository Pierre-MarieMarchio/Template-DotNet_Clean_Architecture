using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal static class BootstrapWriters
{
    private static readonly (Regex Path, string Writer)[] _writtenByBootstrap =
    [
        (Rx(@"^src/Infrastructure/[^/]+\.Infrastructure\.(?:Persistence|Auth)/Migrations/"),
            $"the migrations: {Tool.Name} bootstrap creates them, {Tool.Name} migrations add <Name> adds to them"),
        (Rx(@"^docs/(?:.+/)?index\.md$"), $"{Tool.Name} docs-index"),
        (Rx(@"^tests/Integration/[^/]+\.Api\.IntegrationTests/OpenApi/v1\.json$"), $"{Tool.Name} openapi"),
        (Rx(@"^src/Presentation/[^/]+\.Presentation\.Client/Generated/"), $"{Tool.Name} openapi"),
        (Rx(@"^src/Web/pnpm-lock\.yaml$"), "pnpm install, in src/Web"),
        (Rx(@"^src/Web/packages/api-client/(?:src/generated|openapi)/"), "pnpm api, in src/Web"),
    ];

    private static readonly (Regex Path, string Editor)[] _editedByBootstrap =
    [
        (Rx(@"^\.config/dotnet-tools\.json$"), "bootstrap pins CleanArch.Cli in it"),
        (Rx(@"^src/Web/apps/[^/]+/package\.json$"), "the app's scaffold pins each dependency to the version installed"),
    ];

    public static string? Writer(string path) => _writtenByBootstrap.FirstOrDefault(rule => rule.Path.IsMatch(path)).Writer;

    public static string? Editor(string path) => _editedByBootstrap.FirstOrDefault(rule => rule.Path.IsMatch(path)).Editor;

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
}
