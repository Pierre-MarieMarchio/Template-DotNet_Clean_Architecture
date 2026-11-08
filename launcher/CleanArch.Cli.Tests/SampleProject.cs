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
            "ci": true,
            "sonar": false,
            "agentFiles": true
          },
          "client": true,
          "web": {
            "scope": "acme-shop",
            "apps": [ { "name": "Site", "framework": "vite-react", "audience": "public", "rendering": "spa" } ],
            "packages": [ "ui" ]
          },
          "presentation": [ { "name": "Portal", "framework": "blazor-server" } ]
        }
        """;

    public const string Minimal = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "none" }
        }
        """;

    public const string Api = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "local", "sonar": true },
          "client": true,
          "presentation": [ { "name": "Api", "framework": "api" } ]
        }
        """;

    public const string ApiWithWeb = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "local", "sonar": true },
          "presentation": [ { "name": "Api", "framework": "api" } ],
          "web": {
            "scope": "acme-shop",
            "apps": [ { "name": "Site", "framework": "vite-react", "audience": "public", "rendering": "spa" } ],
            "packages": [ "ui" ]
          }
        }
        """;

    public static string WithOptions(string auth, bool sonar) => $$"""
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "{{auth}}", "sonar": {{(sonar ? "true" : "false")}} }
        }
        """;

    public const string Web = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "local" },
          "web": {
            "scope": "acme-shop",
            "apps": [ { "name": "Site", "framework": "vite-react", "audience": "public", "rendering": "spa" } ],
            "packages": [ "ui" ]
          }
        }
        """;

    public const string Client = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "none" },
          "client": true
        }
        """;

    public static string ApiWith(string auth, bool client) => $$"""
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "{{auth}}" },
          "client": {{(client ? "true" : "false")}},
          "presentation": [ { "name": "Api", "framework": "api" } ]
        }
        """;

    public const string NoApi = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "local" },
          "client": true,
          "presentation": [ { "name": "Portal", "framework": "blazor-server" } ]
        }
        """;

    public static string Named(string name) => $$"""
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "{{name}}",
          "options": { "auth": "local" },
          "client": true,
          "presentation": [ { "name": "Api", "framework": "api" } ]
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
