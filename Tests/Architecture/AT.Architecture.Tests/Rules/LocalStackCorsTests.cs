using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed partial class LocalStackCorsTests
{
    private const string _why =
        "every web app relays /api/ to the API from its own origin, through its development server, its nginx "
        + "or its Node server, so nothing the stack runs calls the API cross-origin; a default origin opens "
        + "CORS on every local stack for a caller that does not exist. A team that needs one sets it in .env.";

    [GeneratedRegex(@"^\s+Cors__AllowedOrigins__\d+:\s*(?<value>.*?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ComposeOrigin();

    [GeneratedRegex(@"^\$\{(?<variable>[A-Z][A-Z0-9_]*):-\}$", RegexOptions.CultureInvariant)]
    private static partial Regex VariableWithoutDefault();

    [GeneratedRegex(@"^\$\{(?<variable>[A-Z][A-Z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex Variable();
    //#if (aspire)

    [GeneratedRegex(@"""Cors__AllowedOrigins__\d+""\s*,\s*(?<value>[^)]*?)\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex AppHostOrigin();
    //#endif

    [Fact]
    public void Compose_GivesTheApiAnOriginOnlyFromAVariableWithoutADefault()
    {
        var values = ComposeOriginValues();

        values.ShouldNotBeEmpty(
            "docker-compose.yml passes Cors__AllowedOrigins__0 from a variable of .env; a parse that finds none is broken.");

        values.Where(value => !VariableWithoutDefault().IsMatch(value)).ShouldBeEmpty(
            $"Compose writes each of these origins when .env sets none: write '${{<VARIABLE>:-}}' instead, because {_why}");
    }

    [Fact]
    public void EnvExample_SetsNoOrigin()
    {
        var variables = ComposeOriginValues()
            .Select(value => Variable().Match(value))
            .Where(match => match.Success)
            .Select(match => match.Groups["variable"].Value)
            .ToHashSet(StringComparer.Ordinal);

        variables.ShouldNotBeEmpty("docker-compose.yml reads Cors__AllowedOrigins__0 from a variable of .env; a parse that finds none is broken.");

        File.ReadAllLines(Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".env.example"))
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2 && variables.Contains(pair[0].Trim()) && pair[1].Trim().Length > 0)
            .Select(pair => $"{pair[0].Trim()}={pair[1].Trim()}")
            .ShouldBeEmpty($".env.example, which a stack starts from, sets these origins: leave each blank, because {_why}");
    }

    [Fact]
    public void TheApisDevelopmentSettings_AllowNoOrigin()
    {
        string api = ProjectReferenceGraph.RootOf(ProjectReferenceGraph.Project("AppTemplate.Api"));

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(api, "appsettings.json"), optional: false, reloadOnChange: false)
            .AddJsonFile(Path.Combine(api, "appsettings.Development.json"), optional: false, reloadOnChange: false)
            .Build();

        configuration.GetSection("Cors:AllowedOrigins").GetChildren().Select(origin => origin.Value).ShouldBeEmpty(
            $"Compose and the app host run the API in Development, so these origins reach every local stack: remove them, because {_why}");
    }
    //#if (aspire)

    [Fact]
    public void TheAppHost_GivesTheApiNoOrigin()
    {
        string appHost = ProjectReferenceGraph.RootOf(ProjectReferenceGraph.SourceProjects.Values
            .Single(project => project.Name.EndsWith(".AppHost", StringComparison.Ordinal)));

        var values = Directory
            .EnumerateFiles(appHost, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => AppHostOrigin().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups["value"].Value)
            .ToList();

        values.ShouldNotBeEmpty(
            "The app host gives the API every setting Compose gives it, Cors__AllowedOrigins__0 among them; a parse that finds none is broken.");

        values.Where(value => value != "string.Empty").ShouldBeEmpty(
            $"The app host gives the API these origins: give it string.Empty, as Compose does when .env sets none, because {_why}");
    }
    //#endif

    private static List<string> ComposeOriginValues() =>
        [.. Directory
            .EnumerateFiles(ProjectReferenceGraph.RepositoryRoot, "docker-compose*.yml")
            .SelectMany(File.ReadAllLines)
            .Select(line => ComposeOrigin().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["value"].Value)];
}
