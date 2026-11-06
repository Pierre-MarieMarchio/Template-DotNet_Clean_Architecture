using CleanArch.Tooling;

namespace CleanArch.Cli.Commands.Kernel;

internal static class ProjectLayout
{
    public static bool HoldsTheApi(LocatedProject project) =>
        project.File.Presentation?.Any(presentation => presentation.Framework == "api") == true;

    public static bool HasWebApps(LocatedProject project) => project.File.Web?.Apps is { Count: > 0 };

    public static bool RendersAWebAppOnTheServer(LocatedProject project) =>
        project.File.Web?.Apps.Any(app => app.Rendering == "ssr") == true;

    public static bool HasAWebAppOnWebAssembly(LocatedProject project) =>
        project.File.Web?.Apps.Any(app => app.Framework == "blazor-wasm") == true;

    public static string Solution(LocatedProject project) => project.File.Name + ".slnx";

    public static string Tasks => Tool("Tasks.cs");

    public static string Tool(string fileName) => Path.Combine("Tools", fileName);

    public static string Web => Path.Combine("Src", "Web");

    public static string ApiClientPackage => Path.Combine(Web, "packages", "api-client");

    public static string WebSchemaScript => Path.Combine(ApiClientPackage, "scripts", "generate.mjs");

    public static string Api(LocatedProject project) => Path.Combine("Src", "Presentation", FolderOf(project, "Api"));

    public static string ApiProject(LocatedProject project) => Path.Combine(Api(project), ProjectName(project, "Api") + ".csproj");

    public static string Persistence(LocatedProject project) =>
        Path.Combine("Src", "Infrastructure", FolderOf(project, "Infrastructure.Persistence"));

    public static string Auth(LocatedProject project) => Path.Combine("Src", "Infrastructure", FolderOf(project, "Infrastructure.Auth"));

    public static string Migrations(LocatedProject project, string context) => Path.Combine(project.Root, context, "Migrations");

    public static IReadOnlyList<string> Contexts(LocatedProject project) =>
        project.File.Options.Auth == "local" ? [Auth(project), Persistence(project)] : [Persistence(project)];

    public static string OpenApiReference(LocatedProject project) =>
        Path.Combine("Tests", "Integration", FolderOf(project, "Api.IntegrationTests"), "OpenApi", "v1.json");

    public static string Client(LocatedProject project) => Path.Combine("Src", "Presentation", FolderOf(project, "Presentation.Client"));

    public static string ArchitectureTests(LocatedProject project) =>
        Path.Combine("Tests", "Architecture", FolderOf(project, "Architecture.Tests"), ProjectName(project, "Architecture.Tests") + ".csproj");

    public static string EndToEndTests(LocatedProject project) =>
        Path.Combine("Tests", "EndToEnd", FolderOf(project, "EndToEnd.Tests"), ProjectName(project, "EndToEnd.Tests") + ".csproj");

    public static string EndToEndComposeProject(LocatedProject project) => ProjectNames.KebabName(project.File.Name) + "-end-to-end";

    private static string FolderOf(LocatedProject project, string part) => ProjectNames.FolderOf(project.File.Name, part);

    private static string ProjectName(LocatedProject project, string part) => $"{project.File.Name}.{part}";
}
