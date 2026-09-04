using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Four properties of the HTTP surface that no compiler and no unit test can see, because each one
/// is about what the API <em>does not</em> do. They are read from the Api project's source rather
/// than from metadata: this test project deliberately does not reference <c>AppTemplate.Api</c>, so
/// that the container tests compose it module by module the way <c>Program.cs</c> does.
/// </summary>
public sealed class HttpSurfaceTests
{
    /// <summary>
    /// The actions allowed to answer an unauthenticated caller, by method name. Authorisation is
    /// default-deny — the fallback policy demands an authenticated user — so this list is the whole
    /// of the public surface, and adding to it is a line in this test rather than an attribute
    /// nobody reviews.
    /// </summary>
    private static readonly string[] _anonymousActions =
    [
        "ConfirmEmail",
        "Login",
        // The external-provider entry point. Anonymous for exactly the reason /auth/login is: it is
        // where a caller who holds no token of ours arrives. What it accepts is a provider's signed
        // id_token, and the refusal path is deliberately as uninformative as the local one's.
        "LoginWithExternalProvider",
        "LoginWithTwoFactor",
        "Logout",
        "Refresh",
        "Register",
        "RequestPasswordReset",
        "ResendConfirmationEmail",
        "ResetPassword",
    ];

    /// <summary>
    /// The minimal endpoints mapped in <c>Program.cs</c> rather than on a controller, which opt out
    /// fluently and so carry no attribute for the scan above to find. Two health probes, and the
    /// OpenAPI document and its reference page, which are mapped in Development only.
    /// </summary>
    private const int _fluentlyAnonymousEndpoints = 4;

    private static readonly Regex _anonymousAction = new(
        @"\[AllowAnonymous\][^{};]*?\b(?:public|internal)\s[^(]*?\b(\w+)\s*\(",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A partial update whose meaning depends on which keys the client happened to send cannot be
    /// checked against an invariant, because an invariant is a property of the whole aggregate. So
    /// an omitted field means <em>absent</em>, not <em>unchanged</em>, and every write says in its
    /// route what it does.
    /// </summary>
    [Fact]
    public void NoEndpoint_AcceptsPatch()
    {
        Offenders("[HttpPatch").ShouldBeEmpty(
            "A PATCH would make an omitted field ambiguous between 'absent' and 'unchanged', which " +
            "is not a distinction an aggregate's invariants can be validated against.");
    }

    /// <summary>
    /// Whether there is a next page is stated once, in <c>PagedResult</c>, in the body every client
    /// already parses. A <c>Link</c> header would be a second statement of the same fact, free to
    /// disagree with the first.
    /// </summary>
    [Fact]
    public void NoResponse_CarriesALinkHeader()
    {
        Offenders("\"Link\"", "HeaderNames.Link").ShouldBeEmpty(
            "Pagination is stated in the body. A second statement in a header is a second thing to " +
            "keep true.");
    }

    /// <summary>
    /// One version ships. Announcing a sunset date nobody has committed to is a promise the
    /// repository cannot keep; delete this test the day a second version exists.
    /// </summary>
    [Fact]
    public void NoResponse_AnnouncesItsOwnDeprecation()
    {
        Offenders("\"Deprecation\"", "\"Sunset\"").ShouldBeEmpty(
            "A Deprecation or Sunset header announces a schedule. There is one version and no " +
            "schedule.");
    }

    /// <summary>
    /// Proves the walk can see an attribute at all — a scan that matched nothing would report no
    /// offenders for every rule above and read as four passes.
    /// </summary>
    [Fact]
    public void EveryAnonymousEndpoint_IsOnTheList()
    {
        var found = ApiSourceFiles()
            .SelectMany(file => _anonymousAction.Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        found.Count.ShouldBeGreaterThanOrEqualTo(
            5,
            "The scan found almost no [AllowAnonymous] action, so it is not reading the controllers " +
            "it is meant to describe and every rule in this class is passing vacuously.");

        found.ShouldBe(
            [.. _anonymousActions.Order(StringComparer.Ordinal)],
            "Authorisation is default-deny, so an anonymous action is an exception that is argued " +
            "for here before it is written.");

        int fluent = ApiSourceFiles()
            .Sum(file => File.ReadAllText(file).Split(".AllowAnonymous()").Length - 1);

        fluent.ShouldBe(
            _fluentlyAnonymousEndpoints,
            "A minimal endpoint opting out fluently carries no attribute, so the scan above cannot " +
            "see it and only this count stands between one more and nobody noticing.");
    }

    private static IReadOnlyList<string> Offenders(params string[] needles)
    {
        return
        [
            .. ApiSourceFiles()
                .Where(file => needles.Any(needle =>
                    File.ReadAllText(file).Contains(needle, StringComparison.Ordinal)))
                .Select(file => Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file))
                .Order(StringComparer.Ordinal)
        ];
    }

    private static List<string> ApiSourceFiles()
    {
        string api = Path.Combine(
            ProjectReferenceGraph.RepositoryRoot, "Src", "Presentation", "AppTemplate.Api");

        var files = Directory
            .EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        files.Count.ShouldBeGreaterThanOrEqualTo(
            50,
            "Far fewer Api source files were found than this template holds, so the walk is not " +
            "reading the project it is meant to describe.");

        return files;
    }
}
