using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ContainerImagePinningTests
{
    private static readonly string[] _dockerfileRoots = ["Src", "Tools"];

    private static readonly Regex _pinned = new(
        @"^[a-z0-9][a-z0-9._/:-]*:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _composeImage = new(
        @"^\s+image:\s*(?<ref>\S+)\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _composeService = new(
        @"^  (?<name>[a-z0-9][a-z0-9_-]*):\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _from = new(
        @"^\s*FROM\s+(?:--platform=\S+\s+)?(?<ref>\S+)(?:\s+AS\s+(?<alias>\S+))?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _syntax = new(
        @"^#\s*syntax\s*=\s*(?<ref>\S+)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    //#if (anyWeb)
    private static readonly Regex _composeInclude = new(
        @"^\s+-\s+(?:path:\s*)?(?<path>\S+compose\.ya?ml)\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    //#endif
    private static readonly Regex _fixtureImage = new(
        @"(?:Builder\(\s*""(?<ref>[^""]+)""|const\s+string\s+\w*Image\w*\s*=\s*""(?<ref>[^""]+)"")",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void EveryComposeImage_IsPinnedByDigest()
    {
        var images = ComposeImages();

        images.ShouldNotBeEmpty("docker-compose.yml runs at least the database; a parse that finds none is broken.");

        images
            .Where(image => !_pinned.IsMatch(image))
            .ShouldBeEmpty(
                /*#if (anyWeb)*/
                "Write each image in docker-compose.yml and in every web app's compose.yml as "
                + "name:tag@sha256:<multi-arch index digest>. "
                /*#else
                "Write each image in docker-compose.yml as name:tag@sha256:<multi-arch index digest>. "
                #endif*/
                + "Resolve it with `docker buildx imagetools inspect name:tag`.");
    }

    [Fact]
    public void EveryDockerfileBase_AndFrontend_IsPinnedByDigest()
    {
        var dockerfiles = Dockerfiles();

        dockerfiles.ShouldNotBeEmpty("the API's Dockerfile exists; a search that finds none is broken.");

        var offenders = new List<string>();

        foreach (string dockerfile in dockerfiles)
        {
            var stages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = File.ReadAllLines(dockerfile);

            if (lines.Length > 0 && _syntax.Match(lines[0]) is { Success: true } syntax
                && !_pinned.IsMatch(syntax.Groups["ref"].Value))
            {
                offenders.Add($"{Relative(dockerfile)}: {lines[0]}");
            }

            foreach (string line in lines)
            {
                var match = _from.Match(line);

                if (!match.Success)
                {
                    continue;
                }

                string reference = match.Groups["ref"].Value;

                if (!stages.Contains(reference) && !_pinned.IsMatch(reference))
                {
                    offenders.Add($"{Relative(dockerfile)}: {line.Trim()}");
                }

                if (match.Groups["alias"].Success)
                {
                    stages.Add(match.Groups["alias"].Value);
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Write each FROM, and the `# syntax=` frontend, as name:tag@sha256:<digest>, literally: "
            + "Dependabot rewrites a literal FROM and skips one built from an ARG.");
    }

    //#if (anyWeb)
    [Fact]
    public void EveryWebApp_IsIncludedInTheRootComposeFile()
    {
        var included = File.ReadAllLines(RootComposeFile())
            .Select(line => _composeInclude.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["path"].Value)
            .ToHashSet(StringComparer.Ordinal);

        WebAppComposeFiles()
            .Select(Relative)
            .Where(file => !included.Contains(file))
            .ShouldBeEmpty(
                "A web app's compose.yml is not under include: in docker-compose.yml, so docker compose up "
                + "never starts it. Add `  - path: Src/Web/apps/<Name>/compose.yml` there.");
    }

    //#endif
    [Fact]
    public void EveryFixtureImage_IsPinned_AndIsOneTheDevelopmentStackRuns()
    {
        var compose = ComposeImages().ToHashSet(StringComparer.Ordinal);
        var fixtures = FixtureImages();

        fixtures.ShouldNotBeEmpty("the integration suites start a PostgreSQL; a search that finds none is broken.");

        fixtures
            .Where(fixture => !_pinned.IsMatch(fixture.Image) || !compose.Contains(fixture.Image))
            .Select(fixture => $"{fixture.File}: {fixture.Image}")
            .ShouldBeEmpty(
                "A fixture runs exactly an image docker-compose.yml runs, tag and digest alike, so the "
                + "store a test exercises is the one a developer has. Copy the image line from there.");
    }

    [Fact]
    public void TheMatchers_AreSensitive()
    {
        const string digest = "sha256:885cf05d376c7cf27afef02073e6bdac3841252537f16e244fd1c1e6a7c99fb1";

        _pinned.IsMatch($"postgres:18.6-alpine3.23@{digest}").ShouldBeTrue();
        _pinned.IsMatch($"mcr.microsoft.com/dotnet/sdk:10.0.302-noble@{digest}").ShouldBeTrue();
        _pinned.IsMatch("postgres:18.6-alpine3.23").ShouldBeFalse();
        _pinned.IsMatch($"postgres@{digest}").ShouldBeFalse("the tag is kept for the reader");

        _from.Match("FROM --platform=$BUILDPLATFORM sdk:1 AS build").Groups["alias"].Value.ShouldBe("build");
        _syntax.IsMatch("# syntax=docker/dockerfile:1").ShouldBeTrue();
        _fixtureImage.IsMatch("new PostgreSqlBuilder(\"postgres:17-alpine\")").ShouldBeTrue();
        _fixtureImage.IsMatch("private const string _storeImage =\n        \"a:b\";").ShouldBeTrue();
        //#if (anyWeb)
        _composeInclude.Match("  - path: Src/Web/apps/Site/compose.yml").Groups["path"].Value.ShouldBe("Src/Web/apps/Site/compose.yml");
        _composeInclude.IsMatch("  - Src/Web/apps/Site/compose.yaml").ShouldBeTrue();
        _composeInclude.IsMatch("  - db").ShouldBeFalse();
        //#endif
    }

    /*#if (anyWeb)*/
    private static List<string> ComposeImages() =>
        [.. new[] { RootComposeFile() }.Concat(WebAppComposeFiles()).SelectMany(ComposeImages)];
    /*#else
    private static List<string> ComposeImages() => ComposeImages(RootComposeFile());
    #endif*/

    private static string RootComposeFile() => Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docker-compose.yml");
    //#if (anyWeb)

    private static List<string> WebAppComposeFiles()
    {
        string apps = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src", "Web", "apps");

        return Directory.Exists(apps)
            ? [.. Directory.EnumerateDirectories(apps)
                .Select(app => Path.Combine(app, "compose.yml"))
                .Where(File.Exists)
                .Order(StringComparer.Ordinal)]
            : [];
    }
    //#endif

    private static List<string> ComposeImages(string composeFile)
    {
        var images = new List<string>();
        string? image = null;
        bool built = false;
        bool inServices = false;

        void Flush()
        {
            if (image is not null && !built)
            {
                images.Add(image);
            }

            image = null;
            built = false;
        }

        foreach (string line in File.ReadAllLines(composeFile))
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line[0] != '#')
            {
                Flush();
                inServices = line.StartsWith("services:", StringComparison.Ordinal);
                continue;
            }

            if (!inServices)
            {
                continue;
            }

            if (_composeService.IsMatch(line))
            {
                Flush();
            }
            else if (_composeImage.Match(line) is { Success: true } match)
            {
                image = match.Groups["ref"].Value;
            }
            else if (line.StartsWith("    build:", StringComparison.Ordinal))
            {
                built = true;
            }
        }

        Flush();

        return images;
    }

    private static List<string> Dockerfiles() =>
        [.. _dockerfileRoots
            .Select(directory => Path.Combine(ProjectReferenceGraph.RepositoryRoot, directory))
            .Where(Directory.Exists)
            .SelectMany(SourceFiles)
            .Where(file => Path.GetFileName(file) is var name
                && (name == "Dockerfile" || name.EndsWith(".Dockerfile", StringComparison.Ordinal)))
            .Where(IsSource)
            .Order(StringComparer.Ordinal)];

    private static List<(string File, string Image)> FixtureImages() =>
        [.. Directory
            .EnumerateFiles(
                Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Tests", "Integration"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(IsSource)
            .SelectMany(file => _fixtureImage
                .Matches(File.ReadAllText(file))
                .Select(match => (Relative(file), match.Groups["ref"].Value)))];

    private static IEnumerable<string> SourceFiles(string directory) =>
        Directory.EnumerateFiles(directory)
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(child => Path.GetFileName(child) is not ("node_modules" or "bin" or "obj"))
                .SelectMany(SourceFiles));

    private static bool IsSource(string file) =>
        !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string file) =>
        Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file).Replace('\\', '/');
}
