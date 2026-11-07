using System.Diagnostics;
using System.Text.RegularExpressions;
using CleanArch.Create.Hosting;
using Shouldly;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests;

public sealed partial class EndToEndTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheLauncher_WritesWhatDotnetNewWrites_ForAProject()
    {
        string package = PackTheTemplates();
        string hive = Path.Combine(_root, "hive");
        string launched = Path.Combine(_root, "launched");
        string direct = Path.Combine(_root, "direct");
        string[] options = ["--auth", "external", "--examples", "notes", "--sonar", "false"];

        Launch(_root, ["new", "-n", "Acme.Shop", "-o", "launched", .. options, "--yes", "--template-source", package, "--hive", hive]);

        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", direct, .. options, "--debug:custom-hive", hive]);

        Dictionary<string, string> written = Tree(launched);
        Dictionary<string, string> expected = Tree(direct);
        written.Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Keys.Order(StringComparer.Ordinal));
        written.Where(file => file.Value != expected[file.Key]).Select(file => file.Key).ShouldBeEmpty("These files differ from what dotnet new wrote.");
    }

    private static void Launch(string workingDirectory, string[] arguments)
    {
        var console = new TestConsole();
        console.Profile.Width = 300;

        int exitCode = new Launcher(console, workingDirectory, interactive: false).Run(arguments);

        exitCode.ShouldBe(0, console.Output);
    }

    private static Dictionary<string, string> Tree(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => Guid().Replace(File.ReadAllText(path), "<guid>"),
                StringComparer.Ordinal);

    private string PackTheTemplates()
    {
        string output = Path.Combine(_root, "package");
        DotNet(Repository.Root, [
            "pack", Path.Combine(Repository.Root, ".template.config", "TemplatePackage.csproj"),
            "--output", output,
            $"-p:ArtifactsPath={Path.Combine(_root, "artifacts")}/",
            "-m:1"]);

        return Directory.EnumerateFiles(output, "*.nupkg").Single();
    }

    private static void DotNet(string workingDirectory, string[] arguments) => Execute(workingDirectory, "dotnet", arguments);

    private static void Execute(string workingDirectory, string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        process.WaitForExit();

        process.ExitCode.ShouldBe(0, $"{executable} {string.Join(' ', arguments)}{Environment.NewLine}{output.Result}{error.Result}");
    }

    [GeneratedRegex("[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex Guid();
}
