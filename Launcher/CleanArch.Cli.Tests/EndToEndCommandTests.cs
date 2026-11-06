using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class EndToEndCommandTests : IDisposable
{
    private static readonly string[] _variables =
    [
        "EXTERNAL_AUTH_AUTHORITY",
        "EXTERNAL_AUTH_AUDIENCE",
        "EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID",
        "EXTERNAL_AUTH_BROWSER_CLIENT_ID",
        "JOBS_FILES_INSPECT_INTERVAL",
    ];

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private readonly Dictionary<string, int> _exitOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _saved = [];

    public EndToEndCommandTests()
    {
        foreach (string variable in _variables)
        {
            _saved[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }

        ExternalCommand.Handler = (call, _, _) =>
        {
            _calls.Add(call);
            return _exitOf.GetValueOrDefault(StepOf(call), CleanArchCommand.Succeeded);
        };
    }

    [Fact]
    public void ExternalAuth_WithAServerRenderedAndAWebAssemblyApp_GivesTheStackEveryClientId()
    {
        using var project = new SampleProject(Project("external", files: false, ("ssr-site", "blazor-server", "ssr"), ("wasm-site", "blazor-wasm", "spa")));

        Run(project).ShouldBe(CleanArchCommand.Succeeded);

        string[] expected =
        [
            "EXTERNAL_AUTH_AUDIENCE", "EXTERNAL_AUTH_AUTHORITY", "EXTERNAL_AUTH_BROWSER_CLIENT_ID", "EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID",
        ];
        _calls.Select(call => Named(call)).ShouldAllBe(names => names.SequenceEqual(expected));
        _calls.First(call => StepOf(call) == "up").Environment!["EXTERNAL_AUTH_AUTHORITY"].ShouldBe("https://identity-provider.invalid/");
    }

    [Fact]
    public void ExternalAuth_WithNoWebApp_GivesTheStackTheAuthorityAndAudienceOnly()
    {
        using var project = new SampleProject(Project("external", files: false));

        Run(project).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Select(call => Named(call)).ShouldAllBe(names => names.SequenceEqual(new[] { "EXTERNAL_AUTH_AUDIENCE", "EXTERNAL_AUTH_AUTHORITY" }));
    }

    [Fact]
    public void Files_WithoutExternalAuth_GiveTheStackTheInspectInterval()
    {
        using var project = new SampleProject(Project("local", files: true));

        Run(project).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Select(call => Named(call)).ShouldAllBe(names => names.SequenceEqual(new[] { "JOBS_FILES_INSPECT_INTERVAL" }));
    }

    [Fact]
    public void WithNeitherExternalAuthNorFiles_TheStackGetsNoVariable()
    {
        using var project = new SampleProject(Project("local", files: false, ("site", "blazor-wasm", "ssr")));

        Run(project).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Where(call => StepOf(call) != "smoke").ShouldAllBe(call => call.Environment == null || call.Environment.Count == 0);
    }

    [Fact]
    public void AVariableAlreadySet_IsLeftAlone_WhileTheOthersAreStillGiven()
    {
        using var project = new SampleProject(Project("external", files: true));
        Environment.SetEnvironmentVariable("EXTERNAL_AUTH_AUDIENCE", "mine");

        Run(project).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Select(call => Named(call)).ShouldAllBe(names => names.SequenceEqual(new[] { "EXTERNAL_AUTH_AUTHORITY", "JOBS_FILES_INSPECT_INTERVAL" }));
    }

    [Fact]
    public void WhenTheTestsFail_TheLogsPrint_ThenTheStackComesDown_AndTheTestsExitCodeIsReturned()
    {
        using var project = new SampleProject(Project("local", files: false));
        _exitOf["tests"] = 9;

        Run(project).ShouldBe(9);

        _calls.Select(StepOf).ShouldBe(["up", "tests", "logs", "down"]);
    }

    [Fact]
    public void WhenTheStackDoesNotStart_TheTestsAreSkipped_ButTheLogsPrintAndTheStackComesDown()
    {
        using var project = new SampleProject(Project("local", files: false));
        _exitOf["up"] = 3;

        Run(project).ShouldBe(3);

        _calls.Select(StepOf).ShouldBe(["up", "logs", "down"]);
    }

    [Fact]
    public void WhenTheTestsFail_KeepStackStillPrintsTheLogsButLeavesTheStackUp()
    {
        using var project = new SampleProject(Project("local", files: false));
        _exitOf["tests"] = 9;

        Run(project, "--keep-stack").ShouldBe(9);

        _calls.Select(StepOf).ShouldBe(["up", "tests", "logs"]);
    }

    [Fact]
    public void TheBrowserSmoke_ReadsTheEnvFile_AndANewFailureNamesWhatItNeeds()
    {
        using var project = new SampleProject(Project("external", files: false, ("site", "vite-react", "spa")));
        _exitOf["smoke"] = 4;

        Run(project).ShouldBe(4);

        ExternalCall smoke = _calls.Single(call => StepOf(call) == "smoke");
        smoke.Environment!["SMOKE_ENV_FILE"].ShouldBe(".env.example");
        smoke.Environment["EXTERNAL_AUTH_AUTHORITY"].ShouldBe("https://identity-provider.invalid/");
        _error.ToString().ShouldContain("pnpm install --frozen-lockfile");
        _error.ToString().ShouldContain("playwright install --only-shell chromium");
        _calls.Select(StepOf).ShouldBe(["up", "tests", "smoke", "logs", "down"]);
    }

    [Fact]
    public void TheConfigurationOption_ReachesTheTestsRun()
    {
        using var project = new SampleProject(Project("local", files: false));

        Run(project, "--configuration", "Release").ShouldBe(CleanArchCommand.Succeeded);

        _calls.Single(call => StepOf(call) == "tests").Arguments.ShouldContain("Release");
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        foreach ((string variable, string? value) in _saved)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }

        _output.Dispose();
        _error.Dispose();
    }

    private int Run(SampleProject project, params string[] options) =>
        CleanArchCommand.Run(["end-to-end", .. options], project.Root, _output, _error);

    private static string[] Named(ExternalCall call) =>
        StepOf(call) == "smoke"
            ? [.. (call.Environment?.Keys ?? []).Where(key => key != "SMOKE_ENV_FILE").Order(StringComparer.Ordinal)]
            : [.. (call.Environment?.Keys ?? []).Order(StringComparer.Ordinal)];

    private static string StepOf(ExternalCall call) =>
        call.FileName switch
        {
            "dotnet" => "tests",
            "docker" => call.Arguments.Contains("up") ? "up" : call.Arguments.Contains("logs") ? "logs" : "down",
            _ => "smoke",
        };

    private static string Project(string auth, bool files, params (string Name, string Framework, string Rendering)[] apps) => $$"""
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "{{auth}}", "files": {{(files ? "true" : "false")}} },
          "presentation": [ { "name": "Api", "framework": "api" } ]
          {{(apps.Length == 0 ? string.Empty : $$"""
          , "web": { "scope": "acme-shop", "apps": [ {{string.Join(", ", apps.Select(app => $$"""{ "name": "{{app.Name}}", "framework": "{{app.Framework}}", "audience": "public", "rendering": "{{app.Rendering}}" }"""))}} ], "packages": [] }
          """)}}
        }
        """;
}
