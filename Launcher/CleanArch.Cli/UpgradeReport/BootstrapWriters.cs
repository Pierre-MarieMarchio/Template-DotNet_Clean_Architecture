using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal static class BootstrapWriters
{
    private static readonly (Regex Path, string Writer)[] _writtenByBootstrap =
    [
        (Rx(@"^Src/Infrastructure/[^/]+\.Infrastructure\.(?:Persistence|Auth)/Migrations/"),
            $"the migrations: {Tool.Name} bootstrap creates them, {Tool.Name} migrations add <Name> adds to them"),
        (Rx(@"^docs/(?:.+/)?index\.md$"), $"{Tool.Name} docs-index"),
        (Rx(@"^Tests/Integration/[^/]+\.Api\.IntegrationTests/OpenApi/v1\.json$"), $"{Tool.Name} openapi"),
        (Rx(@"^Src/Presentation/[^/]+\.Presentation\.Client/Generated/"), $"{Tool.Name} openapi"),
        (Rx(@"^Src/Web/pnpm-lock\.yaml$"), "pnpm install, in Src/Web"),
        (Rx(@"^Src/Web/packages/api-client/(?:src/generated|openapi)/"), "pnpm api, in Src/Web"),
    ];

    private static readonly (Regex Path, string Editor)[] _editedByBootstrap =
    [
        (Rx(@"^\.config/dotnet-tools\.json$"), "bootstrap pins CleanArch.Cli in it"),
        (Rx(@"^Src/Web/apps/[^/]+/package\.json$"), "the app's scaffold pins each dependency to the version installed"),
    ];

    public static string? Writer(string path) => _writtenByBootstrap.FirstOrDefault(rule => rule.Path.IsMatch(path)).Writer;

    public static string? Editor(string path) => _editedByBootstrap.FirstOrDefault(rule => rule.Path.IsMatch(path)).Editor;

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
}
