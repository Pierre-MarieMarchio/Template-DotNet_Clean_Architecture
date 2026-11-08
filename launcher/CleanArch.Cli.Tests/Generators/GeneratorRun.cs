using System.CommandLine;
using System.Text.Json.Nodes;

namespace CleanArch.Cli.Tests.Generators;

internal static class GeneratorRun
{
    internal static (int Code, string Output, string Error) Invoke(Command command, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = command.Parse(args).Invoke(new InvocationConfiguration { Output = output, Error = error });

        return (code, output.ToString(), error.ToString());
    }

    internal static LocatedProject Project(string root)
    {
        string file = Path.Combine(root, ProjectLocator.FileName);
        JsonNode recorded = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))! : new JsonObject { ["name"] = "Acme" };

        return new LocatedProject(
            Path.GetFullPath(root),
            new ProjectFile(
                new TemplateReference("CleanArch.WebApi.Templates", "1.2.3"),
                recorded["name"]!.GetValue<string>(),
                new ProjectOptions(Auth: recorded["options"]?["auth"]?.GetValue<string>(), Database: recorded["options"]?["database"]?.GetValue<string>())));
    }
}
