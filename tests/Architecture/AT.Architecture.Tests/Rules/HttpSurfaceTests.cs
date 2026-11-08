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

    /*#if (!presentationBlazorServer)*/
    private const int _fluentlyAnonymousEndpoints = 4;
    /*#elseif (externalAuth)
    private const int _fluentlyAnonymousEndpoints = 8;
    #else
    private const int _fluentlyAnonymousEndpoints = 7;
    #endif*/

    private static readonly string[] _linkNeedles = ["\"Link\"", "HeaderNames.Link", "Headers.Link"];

    private static readonly string[] _scheduleNeedles =
        ["\"Deprecation\"", "\"Sunset\"", "Headers.Deprecation", "HeaderNames.Sunset"];

    private const string _anonymousOptOut = "AllowAnonymous";

    [Fact]
    public void NoResponse_CarriesALinkHeader()
    {
        Offenders(_linkNeedles).ShouldBeEmpty(
            "Pagination is stated in the body. A second statement in a header is a second thing to " +
            "keep true. Remove the Link header from each file named here.");
    }

    [Fact]
    public void NoResponse_AnnouncesItsOwnDeprecation()
    {
        Offenders(_scheduleNeedles).ShouldBeEmpty(
            "A Deprecation or Sunset header announces a schedule. There is one version and no " +
            "schedule. Remove the header from each file named here.");
    }

    [Fact]
    public void EveryAnonymousEndpoint_IsOnTheList()
    {
        var found = PresentationSourceFiles()
            .SelectMany(file => AnonymousActionsIn(File.ReadAllText(file)))
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
            "for here before it is written. Add the action's name to _anonymousActions in this file, " +
            "or take [AllowAnonymous] off it; remove from the list a name no action carries any more.");

        int fluent = PresentationSourceFiles()
            .Sum(file => FluentlyAnonymousIn(File.ReadAllText(file)));

        fluent.ShouldBe(
            _fluentlyAnonymousEndpoints,
            "A minimal endpoint opting out fluently carries no attribute, so the scan above cannot " +
            "see it and only this count stands between one more and nobody noticing. If the new " +
            "opt-out is deliberate, set _fluentlyAnonymousEndpoints in this file to the count the " +
            "failure reports; if not, remove the '.AllowAnonymous()' call.");
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

    [Fact]
    public void TheAnonymousScan_IsSensitive_AndSeesAnAttributeAndAFluentCallButNotAnAuthorizedAction()
    {
        const string controller = """
            public sealed class SessionsController : ControllerBase
            {
                [AllowAnonymous]
                [HttpPost("login")]
                public async Task<IActionResult> Login(LoginRequest request) => Ok();

                [Authorize]
                [HttpGet("me")]
                public Task<IActionResult> Me() => Ok();

                [AllowAnonymous]
                [HttpPost("refresh")]
                internal Task<IActionResult> Refresh() => Ok();
            }
            """;

        AnonymousActionsIn(controller).ShouldBe(["Login", "Refresh"]);
        AnonymousActionsIn("class C { [Authorize] public Task<IActionResult> Me() => Ok(); }").ShouldBeEmpty();

        FluentlyAnonymousIn("app.MapGet(\"/health\", Ping).AllowAnonymous();\napp.MapGet(\"/me\", Me).RequireAuthorization();")
            .ShouldBe(1);
    }

    [Theory]
    [InlineData("[AllowAnonymous, HttpPost(\"login\")]")]
    [InlineData("[AllowAnonymous()]")]
    [InlineData("[AllowAnonymousAttribute]")]
    [InlineData("[Microsoft.AspNetCore.Authorization.AllowAnonymous]")]
    [InlineData("[HttpPost(\"login\")]\n[AllowAnonymous]\n// a { brace; and a semicolon }")]
    public void TheAnonymousScan_SeesTheAttributeInEveryFormItCanBeWritten(string attributes)
    {
        string controller = $$"""
            public sealed class SessionsController : ControllerBase
            {
                {{attributes}}
                public Task<IActionResult> Login() => Ok();
            }
            """;

        AnonymousActionsIn(controller).ShouldBe(
            ["Login"],
            "An action opts out of authorisation through the attribute in any form C# accepts, so the scan reads the attribute, not one spelling of it.");
    }

    [Fact]
    public void TheAnonymousScan_SeesAWholeControllerOptingOut()
    {
        AnonymousActionsIn("[AllowAnonymous] public sealed class PublicController(ISender sender) : ControllerBase { }")
            .ShouldBe(["PublicController"], "An attribute on the class opts every action of it out.");
    }

    [Fact]
    public void TheAnonymousScan_IgnoresAnOptOutWrittenInACommentOrAString()
    {
        const string controller = """
            public sealed class SessionsController : ControllerBase
            {
                // [AllowAnonymous]
                /* [AllowAnonymous] */
                [Authorize]
                public Task<IActionResult> Me() => Ok("[AllowAnonymous] public Task Login()");
            }
            """;

        AnonymousActionsIn(controller).ShouldBeEmpty("A commented-out attribute opts nothing out.");

        FluentlyAnonymousIn("// app.MapGet(\"/a\", A).AllowAnonymous();\nlog(\".AllowAnonymous()\");\napp.MapGet(\"/b\", B).AllowAnonymous( );")
            .ShouldBe(1, "Only the call in code opts an endpoint out, however it is spaced.");
    }

    private static IEnumerable<string> AnonymousActionsIn(string source) =>
        SourceSyntax.DeclarationsCarrying(source, _anonymousOptOut);

    private static int FluentlyAnonymousIn(string source) => SourceSyntax.InvocationsOf(source, _anonymousOptOut);

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
            ProjectReferenceGraph.RepositoryRoot, "src", "Presentation");

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
