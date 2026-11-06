using System.CommandLine;
using System.Text.RegularExpressions;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Sonar;

internal static class SonarCommand
{
    public const string Name = "sonar";

    public static readonly CommandRegistration Registration = new(Refusal, Build);

    private static readonly Regex _windowsTargetFramework = new(
        @"<TargetFramework>\s*(?<framework>net\d+(\.\d+)*-windows(\d+(\.\d+)*)?)\s*</TargetFramework>",
        RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static string? Refusal(CommandContext context) =>
        ProjectCommand.Refusal(context)
        ?? (context.Project is { } project && project.File.Options.Sonar == true
            ? null
            : $"This project has no sonar command: its {ProjectLocator.FileName} records sonar false.");

    private static Command Build(CommandContext context)
    {
        var sonar = new Command(Name, "Send this project's code to SonarCloud: start the SonarQube service the Compose file holds, then scan from its scanner.");
        if (context.Project is { } project && project.File.Options.Sonar == true)
        {
            sonar.SetAction(parseResult => Run(project, context.Shell(parseResult)));
        }

        return sonar;
    }

    private static int Run(LocatedProject project, Shell shell)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        int exit = shell.Run(new ExternalCall("docker", ["compose", "--profile", "sonar", "up", "--detach", "--wait", "sonarqube"], project.Root));
        if (exit != CleanArchCommand.Succeeded)
        {
            return exit;
        }

        string[]? tests = DockerFreeTests(project, error);
        if (tests is null)
        {
            return CleanArchCommand.Failed;
        }

        return shell.Run(new ExternalCall(
                "docker",
                ["compose", "--profile", "sonar", "run", "--rm", "--build", "sonar-scanner", "/bin/sh", "-c", ScanScriptOf(project, tests)],
                project.Root));
    }

    private static string[]? DockerFreeTests(LocatedProject project, TextWriter error)
    {
        string tests = Path.Combine(project.Root, "Tests");
        var discovered = new List<string>();
        if (Directory.Exists(tests))
        {
            foreach (string path in Directory.EnumerateFiles(tests, "*.csproj", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(path);
                if (!IsDockerFree(text))
                {
                    continue;
                }

                if (!OperatingSystem.IsWindows() && WindowsFramework(text) is not null)
                {
                    continue;
                }

                discovered.Add(Path.GetRelativePath(project.Root, path).Replace('\\', '/'));
            }
        }

        string[] paths = [.. discovered.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
        if (paths.Length == 0)
        {
            error.WriteLine("Discovery found no test project under Tests/. A run over nothing is a false green.");
            return null;
        }

        return paths;
    }

    private static bool IsDockerFree(string projectText) =>
        projectText.Contains("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal)
        && !projectText.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase)
        && !projectText.Contains("Aspire.Hosting.Testing", StringComparison.OrdinalIgnoreCase);

    private static string? WindowsFramework(string projectText) =>
        _windowsTargetFramework.Match(projectText) is { Success: true } match ? match.Groups["framework"].Value : null;

    private static string ScanScriptOf(LocatedProject project, IReadOnlyList<string> testProjects)
    {
        string solution = ProjectLayout.Solution(project);
        IEnumerable<string> tests = testProjects.Select(test => $"""
            dotnet test --project "{test}" \
              --artifacts-path artifacts/sonar \
              --no-build \
              --results-directory artifacts/sonar/TestResults \
              --coverage \
              --coverage-settings coverage.runsettings \
              --coverage-output-format cobertura
            """);
        return $"""
            set -eu
            if [ -z "$SONAR_TOKEN" ]; then
              echo "SONAR_TOKEN is empty. Open http://localhost:9111, generate a token under" >&2
              echo "My Account > Security, and set SONAR_TOKEN in .env." >&2
              exit 1
            fi
            trap 'chown -R "$(stat -c %u:%g /repo/{solution})" /repo/artifacts /repo/.sonarqube 2>/dev/null || true' EXIT
            dotnet tool restore
            dotnet restore {solution} --artifacts-path artifacts/sonar
            dotnet sonarscanner begin \
              /k:"$SONAR_PROJECT_KEY" \
              /d:sonar.token="$SONAR_TOKEN" \
              /d:sonar.host.url="$SONAR_HOST_URL" \
              /d:sonar.cs.cobertura.reportsPaths="artifacts/sonar/TestResults/**/*.cobertura.xml" \
              /d:sonar.cpd.exclusions="**/*.html" \
              /d:sonar.scanner.scanAll=false \
              /d:sonar.scm.disabled=true
            dotnet build {solution} --artifacts-path artifacts/sonar --no-restore
            {string.Join(Environment.NewLine, tests)}
            dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
            """;
    }
}
