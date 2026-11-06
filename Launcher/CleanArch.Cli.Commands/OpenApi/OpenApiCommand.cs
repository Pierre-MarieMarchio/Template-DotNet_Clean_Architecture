using System.CommandLine;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.OpenApi;

internal static class OpenApiCommand
{
    public const string Name = "openapi";

    public static readonly CommandRegistration Registration = new(Refusal, Build);

    private const string _apiClientPackage = "Src/Web/packages/api-client";

    private static readonly string _webSchemaScript = Path.Combine("Src", "Web", "packages", "api-client", "scripts", "generate.mjs");

    private static readonly string _pnpm = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";

    private static string? Refusal(CommandContext context)
    {
        if (context.Project is not { } project)
        {
            return context.RefusedProjectFile?.Message ?? context.OutsideProject;
        }

        return ProjectLayout.HoldsTheApi(project)
            ? null
            : $"This project has no {Name} command: its {ProjectLocator.FileName} records no api presentation, so it holds no HTTP API to describe.";
    }

    private static Command Build(CommandContext context)
    {
        var command = new Command(Name, "Write the OpenAPI reference the API describes itself with, then regenerate every client from it: the .NET client where the project holds one, the web workspace's schema where it holds the api-client package.");
        Option<bool> check = new("--check")
        {
            Description = "Fail where the command would write, instead of writing the OpenAPI reference and what is generated from it.",
        };
        Option<bool> noBuild = new("--no-build")
        {
            Description = "Write the reference from the API's last build instead of building it again.",
        };
        command.Options.Add(check);
        command.Options.Add(noBuild);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, context.Shell(parseResult), parseResult.GetValue(check), parseResult.GetValue(noBuild)));
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, bool checking, bool noBuild)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        string root = project.Root;
        string reference = Path.Combine(root, ProjectLayout.OpenApiReference(project));

        int exit = WriteReference(project, checking, noBuild, shell);
        if (exit == CleanArchCommand.Succeeded && project.File.Client)
        {
            exit = RegenerateClient(project, checking, shell);
        }

        if (exit == CleanArchCommand.Succeeded && File.Exists(Path.Combine(root, _webSchemaScript)))
        {
            exit = checking
                ? shell.Run(new ExternalCall(_pnpm, ["exec", "node", "scripts/generate.mjs", "--check", reference], Path.Combine(root, _apiClientPackage), DirectoryNote: _apiClientPackage))
                : shell.Run(new ExternalCall(_pnpm, ["api", "--no-build"], Path.Combine(root, "Src", "Web"), DirectoryNote: "Src/Web"));
        }

        return exit;
    }

    internal static int WriteReference(LocatedProject project, bool checking, bool noBuild, Shell shell)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        string apiFolder = Path.Combine(project.Root, ProjectLayout.Api(project));
        string apiProject = Path.Combine(project.Root, ProjectLayout.ApiProject(project));
        string reference = Path.Combine(project.Root, ProjectLayout.OpenApiReference(project));
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
        string[] document = [$"-p:OpenApiDocumentsDirectory={documents}", "-p:OpenApiGenerateDocumentsOptions=--file-name v1"];
        if (noBuild && !(Directory.Exists(apiFolder) && Directory.EnumerateFiles(apiFolder, Path.GetFileNameWithoutExtension(apiProject) + ".dll", SearchOption.AllDirectories).Any()))
        {
            error.WriteLine($"--no-build writes the reference from the API's last build, and {Path.GetFileNameWithoutExtension(apiProject)} has none: run `{CleanArchCommand.Name} build` first.");
            return CleanArchCommand.Failed;
        }

        int exit = shell.Run(new ExternalCall(
                "dotnet",
                noBuild
                    ? ["msbuild", apiProject, "-target:GenerateOpenApiDocuments", .. document]
                    : ["build", apiProject, "-p:OpenApiGenerateDocumentsOnBuild=true", .. document],
                project.Root,
                environment));
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

    internal static int RegenerateClient(LocatedProject project, bool checking, Shell shell)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        string root = project.Root;
        string reference = Path.Combine(root, ProjectLayout.OpenApiReference(project));
        string generated = Path.Combine(root, ProjectLayout.Client(project), "Generated");
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
            int exit = shell.Run(new ExternalCall(
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
                    root));
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }

            if (checking && !Directory.Exists(target))
            {
                error.WriteLine($"kiota wrote nothing to {target}.");
                return CleanArchCommand.Failed;
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
        if (!File.Exists(lockFile))
        {
            return;
        }

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
