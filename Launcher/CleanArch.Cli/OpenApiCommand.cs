using System.CommandLine;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CleanArch.Cli;

internal static class OpenApiCommand
{
    public const string Name = "openapi";

    public static readonly CommandRegistration Registration = new(Refusal, Build);

    private static string? Refusal(CommandContext context)
    {
        if (context.Project is not { } project)
        {
            return context.RefusedProjectFile?.Message ?? context.OutsideProject;
        }

        if (!project.File.Presentation?.Any(presentation => presentation.Framework == "api") ?? true)
        {
            return $"This project has no {Name} command: its {ProjectLocator.FileName} records no api presentation, so it holds no HTTP API to describe.";
        }

        if (!project.File.Client)
        {
            return $"This project has no {Name} command: its {ProjectLocator.FileName} records client false, so it holds no .NET client to regenerate.";
        }

        return null;
    }

    private static Command Build(CommandContext context)
    {
        var command = new Command(Name, "Write the OpenAPI reference the API describes itself with, then regenerate the .NET client from it.");
        Option<bool> check = new("--check")
        {
            Description = "Fail where the command would write, instead of writing the OpenAPI reference and the client's Generated folder.",
        };
        command.Options.Add(check);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, parseResult, check));
        }

        return command;
    }

    private static int Run(LocatedProject project, ParseResult parseResult, Option<bool> check)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        bool checking = parseResult.GetValue(check);
        string root = project.Root;
        string apiFolder = Path.Combine(root, ProjectLayout.Api(project));
        string apiProject = Path.Combine(root, ProjectLayout.ApiProject(project));
        string reference = Path.Combine(root, ProjectLayout.OpenApiReference(project));
        string generated = Path.Combine(root, ProjectLayout.Client(project), "Generated");

        int exit = WriteReference(project, apiFolder, apiProject, reference, checking, output, error);
        if (exit != CleanArchCommand.Succeeded)
        {
            return exit;
        }

        return RegenerateClient(project, reference, generated, checking, output, error);
    }

    private static int WriteReference(LocatedProject project, string apiFolder, string apiProject, string reference, bool checking, TextWriter output, TextWriter error)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["ASPNETCORE_ENVIRONMENT"] = "Development" };
        if (project.File.Options.Auth == "external")
        {
            environment["Authentication__External__Authority"] = "https://identity-provider.invalid/";
            environment["Authentication__External__Audience"] = "openapi-document";
        }

        string cache = Path.Combine(apiFolder, "obj", $"{project.File.Name}.Api.OpenApiFiles.cache");
        if (File.Exists(cache))
        {
            File.Delete(cache);
        }

        string documents = checking
            ? Path.Combine(Path.GetTempPath(), "cleanarch-openapi-" + Guid.NewGuid().ToString("N"))
            : Path.GetDirectoryName(reference)!;
        int exit = ExternalCommand.Run(
            new ExternalCall(
                "dotnet",
                [
                    "build", apiProject,
                    "-p:OpenApiGenerateDocumentsOnBuild=true",
                    $"-p:OpenApiDocumentsDirectory={documents}",
                    "-p:OpenApiGenerateDocumentsOptions=--file-name v1",
                ],
                project.Root,
                environment),
            output,
            error);
        if (exit != CleanArchCommand.Succeeded)
        {
            return exit;
        }

        if (!checking)
        {
            if (!File.Exists(reference))
            {
                error.WriteLine($"The build wrote no OpenAPI document at {Shown(project.Root, reference)}.");
                return CleanArchCommand.Failed;
            }

            return CleanArchCommand.Succeeded;
        }

        try
        {
            string staged = Path.Combine(documents, "v1.json");
            if (!File.Exists(reference))
            {
                error.WriteLine($"{Shown(project.Root, reference)} is missing: run `{CleanArchCommand.Name} {Name}` first.");
                return CleanArchCommand.Failed;
            }

            if (!File.Exists(staged) || !File.ReadAllBytes(staged).AsSpan().SequenceEqual(File.ReadAllBytes(reference)))
            {
                error.WriteLine($"{Shown(project.Root, reference)} is not the reference the API describes itself with: run `{CleanArchCommand.Name} {Name}` and commit what it writes.");
                return CleanArchCommand.Failed;
            }

            output.WriteLine($"{Shown(project.Root, reference)} is the OpenAPI reference the API describes itself with.");
            return CleanArchCommand.Succeeded;
        }
        finally
        {
            if (Directory.Exists(documents))
            {
                Directory.Delete(documents, recursive: true);
            }
        }
    }

    private static int RegenerateClient(LocatedProject project, string reference, string generated, bool checking, TextWriter output, TextWriter error)
    {
        string root = project.Root;
        string clientFolder = Path.GetDirectoryName(generated)!;
        if (!Directory.Exists(clientFolder))
        {
            error.WriteLine($"{Shown(root, clientFolder)} does not exist, so there is no client to write: a presentation that calls the API through it brings it.");
            return CleanArchCommand.Failed;
        }

        if (!File.Exists(reference))
        {
            error.WriteLine($"{Shown(root, reference)} is missing: run `{CleanArchCommand.Name} {Name}` first.");
            return CleanArchCommand.Failed;
        }

        string target = checking
            ? Path.Combine(Path.GetTempPath(), "cleanarch-client-" + Guid.NewGuid().ToString("N"))
            : generated;
        try
        {
            int exit = ExternalCommand.Run(
                new ExternalCall(
                    "dotnet",
                    [
                        "tool", "run", "kiota", "generate",
                        "--language", "CSharp",
                        "--openapi", reference,
                        "--output", target,
                        "--class-name", "ApiClient",
                        "--namespace-name", $"{project.File.Name}.Client.Generated",
                        "--exclude-backward-compatible",
                        "--clean-output",
                        "--log-level", "Warning",
                    ],
                    root),
                output,
                error);
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }

            Normalize(target, generated, reference);

            if (!checking)
            {
                output.WriteLine($"Wrote {Shown(root, generated)} from the OpenAPI reference.");
                return CleanArchCommand.Succeeded;
            }

            string[] drifted = [.. TreeDifferences(generated, target)];
            if (drifted.Length > 0)
            {
                error.WriteLine(
                    $"{Shown(root, generated)} differs from what Kiota writes from the OpenAPI reference:" + Environment.NewLine
                    + string.Join(Environment.NewLine, drifted.Select(path => "  " + path)) + Environment.NewLine
                    + $"Run `{CleanArchCommand.Name} {Name}` and commit what it writes.");
                return CleanArchCommand.Failed;
            }

            output.WriteLine($"{Shown(root, generated)} is what Kiota writes from the OpenAPI reference.");
            return CleanArchCommand.Succeeded;
        }
        finally
        {
            if (checking && Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
    }

    private static void Normalize(string target, string generated, string reference)
    {
        File.Delete(Path.Combine(target, ".kiota.log"));

        foreach (string file in Directory.EnumerateFiles(target, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
            File.WriteAllText(file, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        string lockFile = Path.Combine(target, "kiota-lock.json");
        JsonNode settings = JsonNode.Parse(File.ReadAllText(lockFile))!;
        settings["descriptionLocation"] = Path.GetRelativePath(generated, reference).Replace('\\', '/');
        File.WriteAllText(
            lockFile,
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" }) + "\n");
    }

    private static IEnumerable<string> TreeDifferences(string committed, string written)
    {
        string[] Files(string folder) => Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))]
            : [];

        return Files(committed)
            .Union(Files(written), StringComparer.Ordinal)
            .Where(path =>
            {
                string left = Path.Combine(committed, path);
                string right = Path.Combine(written, path);
                return !File.Exists(left) || !File.Exists(right) || !File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
            })
            .Order(StringComparer.Ordinal);
    }

    private static string Shown(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

}
