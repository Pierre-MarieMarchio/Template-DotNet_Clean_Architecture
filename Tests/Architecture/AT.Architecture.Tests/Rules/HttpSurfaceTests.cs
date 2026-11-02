using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class HttpSurfaceTests
{
    private static readonly string[] _anonymousActions =
    [
        //#if (localAuth)
        "ConfirmEmail",
        "Login",
        "LoginWithExternalProvider",
        "LoginWithTwoFactor",
        "Logout",
        "BeginPasskeySignIn",
        "CompletePasskeySignIn",
        "Refresh",
        "Register",
        "RequestPasswordReset",
        "ResendConfirmationEmail",
        "ResetPassword",
        //#endif
    ];

    private const int _fluentlyAnonymousEndpoints = 4;

    private static readonly string[] _linkNeedles = ["\"Link\"", "HeaderNames.Link", "Headers.Link"];

    private static readonly string[] _scheduleNeedles =
        ["\"Deprecation\"", "\"Sunset\"", "Headers.Deprecation", "HeaderNames.Sunset"];

    private static readonly Regex _anonymousAction = new(
        @"\[AllowAnonymous\][^{};]*?\b(?:public|internal)\s[^(]*?\b(\w+)\s*\(",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void NoResponse_CarriesALinkHeader()
    {
        Offenders(_linkNeedles).ShouldBeEmpty(
            "Pagination is stated in the body. A second statement in a header is a second thing to " +
            "keep true.");
    }

    [Fact]
    public void NoResponse_AnnouncesItsOwnDeprecation()
    {
        Offenders(_scheduleNeedles).ShouldBeEmpty(
            "A Deprecation or Sunset header announces a schedule. There is one version and no " +
            "schedule.");
    }

    [Fact]
    public void EveryAnonymousEndpoint_IsOnTheList()
    {
        var found = PresentationSourceFiles()
            .SelectMany(file => _anonymousAction.Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        //#if (localAuth)
        found.Count.ShouldBeGreaterThanOrEqualTo(
            5,
            "The scan found almost no [AllowAnonymous] action, so it is not reading the controllers " +
            "it is meant to describe and every rule in this class is passing vacuously.");
        //#endif

        found.ShouldBe(
            [.. _anonymousActions.Order(StringComparer.Ordinal)],
            "Authorisation is default-deny, so an anonymous action is an exception that is argued " +
            "for here before it is written.");

        int fluent = PresentationSourceFiles()
            .Sum(file => File.ReadAllText(file).Split(".AllowAnonymous()").Length - 1);

        fluent.ShouldBe(
            _fluentlyAnonymousEndpoints,
            "A minimal endpoint opting out fluently carries no attribute, so the scan above cannot " +
            "see it and only this count stands between one more and nobody noticing.");
    }

    [Theory]
    [InlineData("Response.Headers.Link = value;")]
    [InlineData("context.Response.Headers[HeaderNames.Link] = value;")]
    [InlineData("Response.Headers[\"Link\"] = value;")]
    public void TheLinkNeedles_DetectEverySpellingTheyName(string source) =>
        Mentions(source, _linkNeedles).ShouldBeTrue();

    [Theory]
    [InlineData("Response.Headers[\"Sunset\"] = value;")]
    [InlineData("Response.Headers.Deprecation = value;")]
    public void TheScheduleNeedles_DetectEverySpellingTheyName(string source) =>
        Mentions(source, _scheduleNeedles).ShouldBeTrue();

    [Theory]
    [InlineData("Response.Headers.ETag = tag;")]
    public void TheNeedles_LeaveOrdinaryCodeAlone(string source)
    {
        Mentions(source, _linkNeedles).ShouldBeFalse();
        Mentions(source, _scheduleNeedles).ShouldBeFalse();
    }

    private static bool Mentions(string content, string[] needles) =>
        needles.Any(needle => content.Contains(needle, StringComparison.Ordinal));

    private static IReadOnlyList<string> Offenders(params string[] needles)
    {
        return
        [
            .. PresentationSourceFiles()
                .Where(file => Mentions(File.ReadAllText(file), needles))
                .Select(file => Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file))
                .Order(StringComparer.Ordinal)
        ];
    }

    private static List<string> PresentationSourceFiles()
    {
        string presentation = Path.Combine(
            ProjectReferenceGraph.RepositoryRoot, "Src", "Presentation");

        var files = Directory
            .EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        files.Count.ShouldBeGreaterThanOrEqualTo(
            69,
            "Far fewer presentation source files were found than this template holds, so the walk " +
            "is not reading the projects it is meant to describe.");

        return files;
    }
}
