using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CleanArch.Cli.Commands.Kernel;

internal static class ApiClient
{
    private static readonly JsonSerializerOptions _lockFile = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" };

    public static string Generated(LocatedProject project) => Path.Combine(project.Root, ProjectLayout.Client(project), "Generated");

    private static string? Refusal(LocatedProject project, string purpose)
    {
        string client = Path.Combine(project.Root, ProjectLayout.Client(project));
        if (!Directory.Exists(client))
        {
            return $"{ProjectPath.Relative(project.Root, client)} does not exist, so there is no client to {purpose}: a presentation that calls the API through it brings it.";
        }

        return File.Exists(OpenApiReference.PathOf(project)) ? null : OpenApiReference.Missing(project);
    }

    private static ExternalCall Generation(LocatedProject project, string reference, string output) =>
        Dotnet.Call(
            project.Root,
            "tool", "run", "kiota", "generate",
            "--language", "CSharp",
            "--openapi", reference,
            "--output", output,
            "--class-name", "ApiClient",
            "--namespace-name", $"{project.File.Name}.Presentation.Client.Generated",
            "--exclude-backward-compatible",
            "--clean-output",
            "--log-level", "Warning");

    public static int Write(LocatedProject project, Shell shell)
    {
        if (Refusal(project, "write") is { } refusal)
        {
            return shell.Fail(refusal);
        }

        string generated = Generated(project);
        int exit = shell.Run(Generation(project, OpenApiReference.PathOf(project), generated));
        if (exit != ExitCode.Succeeded)
        {
            return exit;
        }

        Normalise(project, generated);
        shell.Output.WriteLine($"Wrote {ProjectPath.Relative(project.Root, generated)} from the OpenAPI reference.");
        return ExitCode.Succeeded;
    }

    public static int Check(LocatedProject project, Shell shell, string reference, string scratchPrefix, string purpose)
    {
        if (Refusal(project, purpose) is { } refusal)
        {
            return shell.Fail(refusal);
        }

        string written = Path.Combine(Path.GetTempPath(), scratchPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            return Compare(project, shell, reference, written);
        }
        finally
        {
            if (Directory.Exists(written))
            {
                Directory.Delete(written, recursive: true);
            }
        }
    }

    private static int Compare(LocatedProject project, Shell shell, string reference, string written)
    {
        int exit = shell.Run(Generation(project, reference, written));
        if (exit != ExitCode.Succeeded)
        {
            return exit;
        }

        if (!Directory.Exists(written))
        {
            return shell.Fail($"kiota wrote nothing to {written}.");
        }

        Normalise(project, written);
        return Drift(project, written) is { } drift ? shell.Fail(drift) : ExitCode.Succeeded;
    }

    private static void Normalise(LocatedProject project, string written)
    {
        File.Delete(Path.Combine(written, ".kiota.log"));
        foreach (string file in Directory.EnumerateFiles(written, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
            File.WriteAllText(file, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        string lockFile = Path.Combine(written, "kiota-lock.json");
        if (File.Exists(lockFile))
        {
            JsonNode settings = JsonNode.Parse(File.ReadAllText(lockFile))!;
            settings["descriptionLocation"] = ProjectPath.Relative(Generated(project), OpenApiReference.PathOf(project));
            File.WriteAllText(lockFile, settings.ToJsonString(_lockFile) + "\n");
        }
    }

    private static string? Drift(LocatedProject project, string written)
    {
        string generated = Generated(project);
        string[] drifted = [.. FilesUnder(generated)
            .Union(FilesUnder(written), StringComparer.Ordinal)
            .Where(path => Differs(Path.Combine(generated, path), Path.Combine(written, path)))
            .Order(StringComparer.Ordinal)];
        return drifted.Length == 0
            ? null
            : $"{ProjectPath.Relative(project.Root, generated)} differs from what Kiota writes from the OpenAPI reference:" + Environment.NewLine
                + string.Join(Environment.NewLine, drifted.Select(path => "  " + path)) + Environment.NewLine
                + $"Run `{Tool.Name} openapi` and commit what it writes.";
    }

    private static IEnumerable<string> FilesUnder(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(file => ProjectPath.Relative(folder, file))
            : [];

    private static bool Differs(string committed, string written) =>
        !File.Exists(committed) || !File.Exists(written) || !File.ReadAllBytes(committed).AsSpan().SequenceEqual(File.ReadAllBytes(written));
}
