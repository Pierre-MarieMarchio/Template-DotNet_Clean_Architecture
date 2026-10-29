namespace CleanArch.Cli.Tests;

internal sealed class SampleProject : IDisposable
{
    public const string Full = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": {
            "auth": "local",
            "database": "postgresql",
            "examples": "all",
            "files": true,
            "jobs": "api",
            "ci": true,
            "sonar": false,
            "agentFiles": true
          },
          "client": true,
          "web": {
            "scope": "acme-shop",
            "apps": [ { "name": "Site", "framework": "vite-react", "audience": "public", "rendering": "spa" } ],
            "packages": [ { "name": "ui" } ]
          },
          "presentation": { "name": "Portal", "framework": "blazor-server" }
        }
        """;

    public const string Minimal = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "none" }
        }
        """;

    public SampleProject(string json)
    {
        Root = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        File.WriteAllText(Path.Combine(Root, ProjectLocator.FileName), json);
    }

    public string Root { get; }

    public string Folder(string relative)
    {
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
