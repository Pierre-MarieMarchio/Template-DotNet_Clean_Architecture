using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace CleanArch.Create.Manifests;

public sealed record TemplateKind(string ShortName, string Folder)
{
    public static readonly TemplateKind Project = new("cleanarch-webapi", ".template.config/");
    public static readonly TemplateKind WebApp = new("cleanarch-webapp", ".template.config/webapp/.template.config/");
    public static readonly TemplateKind WebPackage = new("cleanarch-webpackage", ".template.config/webpackage/.template.config/");
}

public sealed class TemplateSource
{
    public const string PackageId = "CleanArch.WebApi.Templates";

    private const string _packageContent = "content/";

    private readonly Func<string, string?> _read;

    private TemplateSource(string? localPath, Func<string, string?> read)
    {
        LocalPath = localPath;
        _read = read;
    }

    public string? LocalPath { get; }

    public static TemplateSource Embedded()
    {
        Assembly assembly = typeof(TemplateSource).Assembly;

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
            return new TemplateSource(path, relative =>
            {
                using ZipArchive archive = ZipFile.OpenRead(path);
                ZipArchiveEntry? entry = archive.GetEntry(_packageContent + relative);
                if (entry is null)
                {
                    return null;
                }

                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            });
        }

        throw new LauncherException($"--template-source '{path}' is neither a folder nor a .nupkg file.");
    }

    public TemplateManifest Manifest(TemplateKind kind)
    {
        string template = _read(kind.Folder + "template.json")
            ?? throw new LauncherException($"The template source holds no {kind.Folder}template.json for {kind.ShortName}.");

        return TemplateManifest.Read(template, _read(kind.Folder + "dotnetcli.host.json"), _read(kind.Folder + "ide.host.json"));
    }
}
