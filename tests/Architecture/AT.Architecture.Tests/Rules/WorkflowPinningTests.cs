using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class WorkflowPinningTests
{
    private const string _containerStep = "docker://";

    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(1);

    private static readonly Regex _uses = new(@"^\s*(?:-\s+)?uses:\s*(?<quote>['""]?)(?<ref>[^'""\s#]+)\k<quote>", RegexOptions.None, _budget);

    private static readonly Regex _pinnedAction = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$", RegexOptions.None, _budget);

    private static readonly Regex _pinnedImage = new(@"^[a-z0-9][a-z0-9._/:-]*:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}$", RegexOptions.None, _budget);

    private static readonly Regex _image = new(@"^\s*(?:-\s+)?(?:image|container):\s*(?<quote>['""]?)(?<ref>[^'""\s#{]+)\k<quote>\s*(?:#.*)?$", RegexOptions.None, _budget);

    private static readonly Regex _driverImage = new(@"(?:^|[\s,])image=(?<ref>[^\s,'""]+)", RegexOptions.None, _budget);

    [Fact]
    public void EveryActionAWorkflowUses_IsPinnedToACommit()
    {
        var actions = References(_uses).Where(action => !action.Reference.StartsWith(_containerStep, StringComparison.Ordinal)).ToList();

        //#if (ci)
        actions.ShouldNotBeEmpty("ci.yml checks out the repository; a search of .github/workflows that finds no action is broken.");
        //#endif
        actions
            .Where(action => !IsPinnedAction(action.Reference))
            .Select(action => $"{action.Where}: {action.Reference}")
            .ShouldBeEmpty(
                "Write each action as owner/repo@<40-character commit SHA> # <tag>, as Dependabot keeps it: a tag or a branch "
                + "moves under the workflow. A local action, ./path, needs no pin.");
    }

    [Fact]
    public void EveryImageAWorkflowRuns_IsPinnedByDigest()
    {
        References(_image)
            .Concat(References(_driverImage))
            .Concat(References(_uses).Where(action => action.Reference.StartsWith(_containerStep, StringComparison.Ordinal))
                .Select(action => action with { Reference = action.Reference[_containerStep.Length..] }))
            .Where(image => !_pinnedImage.IsMatch(image.Reference))
            .Select(image => $"{image.Where}: {image.Reference}")
            .ShouldBeEmpty("Write each image a workflow runs, a container:, a service, a docker:// step or the BuildKit driver, as name:tag@sha256:<digest>.");
    }

    [Fact]
    public void TheMatchers_AreSensitive()
    {
        const string sha = "3d3c42e5aac5ba805825da76410c181273ba90b1";
        const string digest = "sha256:cec9f139f45e93c5c69c60f8b07cfad9f43f4ef6b6a6cd917527fea5ff2e3dea";

        Reference(_uses, $"      - uses: actions/checkout@{sha} # v7.0.1").ShouldBe($"actions/checkout@{sha}");
        Reference(_uses, $"        uses: 'github/codeql-action/init@{sha}'").ShouldBe($"github/codeql-action/init@{sha}");
        IsPinnedAction($"actions/checkout@{sha}").ShouldBeTrue();
        IsPinnedAction($"github/codeql-action/analyze@{sha}").ShouldBeTrue();
        IsPinnedAction("./.github/actions/setup").ShouldBeTrue("a local action is the repository's own code");
        IsPinnedAction("actions/checkout@v7").ShouldBeFalse();
        IsPinnedAction("actions/checkout@main").ShouldBeFalse();
        IsPinnedAction($"actions/checkout@{sha[..7]}").ShouldBeFalse("a short SHA can collide");
        IsPinnedAction($"docker://alpine:3.22@{digest}").ShouldBeFalse("a container step is an image, which the image rule judges");

        Reference(_image, "        image: postgres:18").ShouldBe("postgres:18");
        Reference(_image, "    container: node:24 # the runtime").ShouldBe("node:24");
        Reference(_image, "  image:").ShouldBeNull("a job named image is not an image");
        Reference(_image, "        image: ${{ matrix.image }}").ShouldBeNull("an image built from an expression is the matrix's");
        Reference(_driverImage, $"          driver-opts: image=moby/buildkit:v0.33.1@{digest}").ShouldBe($"moby/buildkit:v0.33.1@{digest}");
        _pinnedImage.IsMatch($"moby/buildkit:v0.33.1@{digest}").ShouldBeTrue();
        _pinnedImage.IsMatch("moby/buildkit:v0.33.1").ShouldBeFalse();
        _pinnedImage.IsMatch($"moby/buildkit@{digest}").ShouldBeFalse("the tag is kept for the reader");
    }

    private static bool IsPinnedAction(string reference) =>
        reference.StartsWith("./", StringComparison.Ordinal) || _pinnedAction.IsMatch(reference);

    private static string? Reference(Regex matcher, string line) =>
        matcher.Match(line) is { Success: true } match ? match.Groups["ref"].Value : null;

    private static IEnumerable<(string Where, string Reference)> References(Regex matcher) =>
        Workflows().SelectMany(workflow => File.ReadLines(workflow)
            .Select((line, index) => (Where: $"{RepositoryTree.Relative(workflow)}:{index + 1}", Reference: Reference(matcher, line)))
            .Where(found => found.Reference is not null)
            .Select(found => (found.Where, found.Reference!)));

    private static IEnumerable<string> Workflows()
    {
        string folder = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".github", "workflows");

        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Where(file => Path.GetExtension(file) is ".yml" or ".yaml")
                .Order(StringComparer.Ordinal)
            : [];
    }
}
