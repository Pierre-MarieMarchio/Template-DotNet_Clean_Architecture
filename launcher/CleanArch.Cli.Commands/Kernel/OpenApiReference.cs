namespace CleanArch.Cli.Commands.Kernel;

internal static class OpenApiReference
{
    public static string PathOf(LocatedProject project) => Path.Combine(project.Root, ProjectLayout.OpenApiReference(project));

    public static string Missing(LocatedProject project) => $"{Shown(project)} is missing: run `{Tool.Name} openapi` first.";

    public static int Write(LocatedProject project, Shell shell, bool noBuild)
    {
        int exit = Generate(project, shell, noBuild, Path.GetDirectoryName(PathOf(project))!);
        if (exit != ExitCode.Succeeded)
        {
            return exit;
        }

        return File.Exists(PathOf(project))
            ? ExitCode.Succeeded
            : shell.Fail($"The build wrote no OpenAPI document at {Shown(project)}.");
    }

    public static int Check(LocatedProject project, Shell shell, bool noBuild)
    {
        string documents = Path.Combine(Path.GetTempPath(), "cleanarch-openapi-" + Guid.NewGuid().ToString("N"));
        try
        {
            int exit = Generate(project, shell, noBuild, documents);
            return exit == ExitCode.Succeeded
                ? Compare(project, shell, Path.Combine(documents, "v1.json"))
                : exit;
        }
        finally
        {
            if (Directory.Exists(documents))
            {
                Directory.Delete(documents, recursive: true);
            }
        }
    }

    private static int Generate(LocatedProject project, Shell shell, bool noBuild, string documents)
    {
        string apiFolder = Path.Combine(project.Root, ProjectLayout.Api(project));
        string apiProject = Path.Combine(project.Root, ProjectLayout.ApiProject(project));
        string apiName = Path.GetFileNameWithoutExtension(apiProject);
        string cache = Path.Combine(apiFolder, "obj", $"{project.File.Name}.Api.OpenApiFiles.cache");
        if (File.Exists(cache))
        {
            File.Delete(cache);
        }

        if (noBuild && !(Directory.Exists(apiFolder) && Directory.EnumerateFiles(apiFolder, apiName + ".dll", SearchOption.AllDirectories).Any()))
        {
            return shell.Fail($"--no-build writes the reference from the API's last build, and {apiName} has none: run `{Tool.Name} build` first.");
        }

        string[] document = [$"-p:OpenApiDocumentsDirectory={documents}", "-p:OpenApiGenerateDocumentsOptions=--file-name v1"];
        string[] arguments = noBuild
            ? ["msbuild", apiProject, "-target:GenerateOpenApiDocuments", .. document]
            : ["build", apiProject, "-p:OpenApiGenerateDocumentsOnBuild=true", .. document];
        return shell.Run(Dotnet.Call(project.Root, arguments) with { Environment = EnvironmentOf(project) });
    }

    private static Dictionary<string, string> EnvironmentOf(LocatedProject project)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["ASPNETCORE_ENVIRONMENT"] = "Development" };
        if (project.File.Options.Auth == "external")
        {
            environment["Authentication__External__Authority"] = "https://identity-provider.invalid/";
            environment["Authentication__External__Audience"] = "openapi-document";
        }

        return environment;
    }

    private static int Compare(LocatedProject project, Shell shell, string staged)
    {
        string reference = PathOf(project);
        if (!File.Exists(reference))
        {
            return shell.Fail(Missing(project));
        }

        if (!File.Exists(staged) || !File.ReadAllBytes(staged).AsSpan().SequenceEqual(File.ReadAllBytes(reference)))
        {
            return shell.Fail($"{Shown(project)} is not the reference the API describes itself with: run `{Tool.Name} openapi` and commit what it writes.");
        }

        shell.Output.WriteLine($"{Shown(project)} is the OpenAPI reference the API describes itself with.");
        return ExitCode.Succeeded;
    }

    private static string Shown(LocatedProject project) => ProjectPath.Shown(ProjectLayout.OpenApiReference(project));
}
