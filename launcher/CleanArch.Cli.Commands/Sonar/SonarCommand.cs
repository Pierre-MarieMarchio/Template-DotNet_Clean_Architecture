using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Sonar;

internal static class SonarCommand
{
    public const string Name = "sonar";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Requiring(Refusal), Create);

    private static string? Refusal(LocatedProject project) =>
        project.File.Options.Sonar == true ? null : $"This project has no sonar command: its {ProjectLocator.FileName} records sonar false.";

    private static Command Create(CommandContext context)
    {
        var sonar = new Command(Name, "Send this project's code to SonarCloud: start the SonarQube service the Compose file holds, then scan from its scanner.");
        if (context.Project is { } project)
        {
            sonar.SetAction(parseResult => Run(project, context.Shell(parseResult)));
        }

        return sonar;
    }

    private static int Run(LocatedProject project, Shell shell)
    {
        int exit = shell.Run(Docker.Compose(project.Root, "--profile", "sonar", "up", "--detach", "--wait", "sonarqube"));
        if (exit != ExitCode.Succeeded)
        {
            return exit;
        }

        string[] tests = DockerFreeTests(project);
        return tests.Length == 0
            ? shell.Fail(TestProjects.NoneFound)
            : shell.Run(Docker.Compose(project.Root, "--profile", "sonar", "run", "--rm", "--build", "sonar-scanner", "/bin/sh", "-c", ScanScriptOf(project, tests)));
    }

    private static string[] DockerFreeTests(LocatedProject project)
    {
        string[] tests = TestProjects.Under(project.Root);
        return [.. tests
            .Where(TestProjects.IsDockerFree)
            .Except(TestProjects.SkippedHere(tests), StringComparer.Ordinal)
            .Select(test => ProjectPath.Relative(project.Root, test))];
    }

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
