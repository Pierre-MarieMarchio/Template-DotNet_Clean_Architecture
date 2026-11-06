using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CleanArch.Cli.Commands.Kernel;

internal static partial class WebAppScaffold
{
    public static void Create(Shell shell, string web, string folder)
    {
        string app = Path.GetFileName(folder);
        ScaffoldDescriptor descriptor = ScaffoldDescriptor.Read(folder);
        string scratch = Path.Combine(web, ".web-scaffold-" + Guid.NewGuid().ToString("N"));
        string starter = Path.Combine(scratch, descriptor.Name);
        string overlay = Path.Combine(folder, "overlay");
        try
        {
            Directory.CreateDirectory(scratch);
            RunTheOfficialCli(shell, descriptor, scratch, app);
            if (!File.Exists(Path.Combine(starter, "package.json")))
            {
                throw new InvalidOperationException($"{descriptor.Package} wrote no package.json in {starter}, so Src/Web/apps/{app} is left unchanged.");
            }

            Shape(descriptor, starter, overlay);
            ScaffoldFiles.Copy(starter, folder, overwrite: false);
        }
        finally
        {
            ScaffoldFiles.DeleteFolder(scratch);
        }

        ScaffoldFiles.DeleteFolder(overlay);
        ScaffoldDescriptor.Delete(folder);
        shell.Output.WriteLine($"Created Src/Web/apps/{app} from {descriptor.Package} and its overlay.");
    }

    public static bool PinInstalledVersions(string folder)
    {
        string manifest = Path.Combine(folder, "package.json");
        JsonObject package = ScaffoldJson.Read(manifest);
        bool changed = false;
        foreach (JsonObject dependencies in new[] { package["dependencies"], package["devDependencies"] }.OfType<JsonObject>())
        {
            foreach ((string dependency, string declared) in Ranges(dependencies))
            {
                dependencies[dependency] = InstalledVersion(folder, manifest, dependency, declared);
                changed = true;
            }
        }

        if (changed)
        {
            ScaffoldJson.Write(manifest, package);
        }

        return changed;
    }

    private static void RunTheOfficialCli(Shell shell, ScaffoldDescriptor descriptor, string scratch, string app)
    {
        ExternalCall call = Pnpm.Call(scratch, ["dlx", descriptor.Package, .. descriptor.Arguments(descriptor.Name)]) with { CloseInput = true };
        Scaffolds.Required(
            shell,
            call,
            $"Creating Src/Web/apps/{app} runs {descriptor.Package}, the framework's official CLI, which pnpm fetches from the npm registry, "
            + "so this step needs Node.js, pnpm (`corepack enable` once per machine) and the network; the output above says which failed. "
            + "The app's folder is unchanged, so running this again once the cause is fixed creates it.");
    }

    private static void Shape(ScaffoldDescriptor descriptor, string starter, string overlay)
    {
        if (Directory.Exists(overlay))
        {
            ScaffoldFiles.Copy(overlay, starter, overwrite: true);
        }

        foreach (string removed in descriptor.Remove.Select(path => Path.Combine(starter, path)))
        {
            ScaffoldFiles.DeleteFolder(removed);
            File.Delete(removed);
        }

        foreach ((string file, JsonNode? patch) in descriptor.Merge)
        {
            string path = Path.Combine(starter, file);
            JsonObject merged = ScaffoldJson.Read(path);
            ScaffoldJson.Merge(merged, patch?.AsObject() ?? []);
            ScaffoldJson.Write(path, merged);
        }
    }

    private static IEnumerable<(string Dependency, string Declared)> Ranges(JsonObject dependencies) =>
        [.. dependencies
            .Select(dependency => (dependency.Key, Declared: dependency.Value?.GetValue<string>() ?? string.Empty))
            .Where(dependency => !dependency.Declared.StartsWith("workspace:", StringComparison.Ordinal) && !ExactVersion().IsMatch(dependency.Declared))];

    private static string InstalledVersion(string folder, string manifest, string dependency, string declared)
    {
        string installed = Path.Combine(folder, "node_modules", dependency, "package.json");
        return File.Exists(installed)
            ? ScaffoldJson.Text(ScaffoldJson.Read(installed), "version", installed)
            : throw new InvalidOperationException($"{manifest} declares {dependency} {declared}, and pnpm installed no version of it.");
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex ExactVersion();
}
