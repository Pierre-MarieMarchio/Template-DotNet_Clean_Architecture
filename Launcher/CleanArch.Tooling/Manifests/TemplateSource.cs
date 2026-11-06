using System.Reflection;
using System.Text;
using CleanArch.Tooling.Failures;

namespace CleanArch.Tooling.Manifests;

public sealed record TemplateKind(string ShortName, string Folder)
{
    public static readonly TemplateKind Project = new("cleanarch-webapi", ".template.config/");
    public static readonly TemplateKind WebApp = new("cleanarch-webapp", ".template.config/webapp/.template.config/");
    public static readonly TemplateKind WebPackage = new("cleanarch-webpackage", ".template.config/webpackage/.template.config/");
    public static readonly TemplateKind Presentation = new("cleanarch-presentation", ".template.config/presentation/.template.config/");
}

public sealed class TemplateSource
{
    public const string PackageId = "CleanArch.WebApi.Templates";

    private readonly Func<string, string?> _read;

    private TemplateSource(string? localPath, Func<string, string?> read)
    {
        LocalPath = localPath;
        _read = read;
    }

    public string? LocalPath { get; }

    public static string Installable(string? templateVersion) =>
        templateVersion ?? throw new LauncherException("This template source has no templateVersion, so there's no package version to install.");

    public static TemplateSource Embedded(Assembly assembly)
    {
        return new TemplateSource(null, path =>
        {
            using Stream? stream = assembly.GetManifestResourceStream(path);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    public static TemplateSource FromPath(string path)
    {
        if (Directory.Exists(path))
        {
            if (!File.Exists(Path.Combine(path, TemplateKind.Project.Folder, "template.json")))
            {
                throw new LauncherException($"'{path}' has no .template.config/template.json. --template-source takes a checkout of the template or its .nupkg.");
            }

            return new TemplateSource(path, relative =>
            {
                string file = Path.Combine(path, relative);
                return File.Exists(file) ? File.ReadAllText(file) : null;
            });
        }

        if (File.Exists(path) && path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            return new TemplateSource(path, relative => NuGetPackage.Entry(path, NuGetPackage.ContentFolder + relative));
        }

        throw new LauncherException($"--template-source '{path}' is neither a folder nor a .nupkg file.");
    }

    public TemplateManifest Manifest(TemplateKind kind) =>
        ManifestIn(kind.Folder, kind.ShortName);

    public TemplateManifest ManifestIn(string folder, string? shortName = null)
    {
        string template = _read(folder + "template.json")
            ?? throw new LauncherException($"The template source holds no {folder}template.json for {shortName ?? folder}.");

        return TemplateManifest.Read(template, _read(folder + "dotnetcli.host.json"), _read(folder + "ide.host.json"));
    }
}
