using System.Reflection;
using System.Text.RegularExpressions;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Architecture.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ApiRouteTableTests
{
    private const string _routesHeading = "## Routes";

    private static readonly Regex _sectionPrefix = new(
        @"^Under `(?<prefix>/[^`]+)`",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _sectionStart = new(
        @"(?m)^### ",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _routeConstraint = new(
        @"\{(?<name>\w+)(?::[^}]*)?\}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _versionSegment = new(
        @"v\{version(?::\w+)?\}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void EveryRouteTheApiExposes_HasARowInTheApiReference()
    {
        var documented = DocumentedRoutes(ApiReference());

        string[] unlisted =
        [
            .. ExposedRoutes(ApiControllers())
                .Where(route => !documented.Contains(route))
                .Select(AddRow),
        ];

        unlisted.ShouldBeEmpty(
            "A caller finds the API's routes in the tables under '## Routes' of docs/reference/api.md, "
            + "and these are exposed but not listed. Add each row to the table of its feature, with its "
            + "success status and a note; under a section's 'Under `<prefix>`' line a route is written "
            + "relative to that prefix.");
    }

    [Fact]
    public void EveryRowOfTheApiReference_IsARouteTheApiExposes()
    {
        var exposed = ExposedRoutes(ApiControllers());

        string[] invented =
        [
            .. DocumentedRoutes(ApiReference())
                .Where(route => !exposed.Contains(route))
                .Select(route => $"remove the row of {route}"),
        ];

        invented.ShouldBeEmpty(
            "The tables under '## Routes' of docs/reference/api.md list routes no controller exposes, "
            + "so a caller is told to call something that answers 404. Correct the row, or remove it "
            + "with the route it described.");
    }

    //#if (!publicApi)
    [Fact]
    public void TheScans_ReadTheRoutesTheyAreAssertedOver()
    {
        ExposedRoutes(ApiControllers()).ShouldNotBeEmpty(
            "No route was found on the API's controllers, so the reflection reads nothing and the "
            + "comparison passes whatever the controllers expose.");
        DocumentedRoutes(ApiReference()).ShouldNotBeEmpty(
            "No row was parsed out of the route tables, so they are read with the wrong shape.");
    }

    //#endif
    [Fact]
    public void TheComparison_IsSensitive()
    {
        var exposed = ExposedRoutes([typeof(DeliberateController)]);

        exposed.ShouldBe(
            [
                "DELETE /api/v1/gadgets/{gadgetId}",
                "GET /api/v1/gadgets",
                "GET /api/v1/gadgets/{gadgetId}",
                "HEAD /api/v1/gadgets/{gadgetId}",
                "POST /api/v1/gadgets/{gadgetId}/ship",
                "POST /api/v1/ungrouped",
            ],
            ignoreOrder: true);

        var documented = DocumentedRoutes(
            """
            ## Routes

            ### Health

            | Method | Route | Success | Notes |
            |---|---|---|---|
            | GET | `/health` | 200 | Anonymous. |

            ### Gadgets

            Under `/api/v1/gadgets`; † marks a limited route.

            | Method | Route | Success | Notes |
            |---|---|---|---|
            | GET | `/` | 200 | |
            | GET, HEAD | `/{gadgetId}` | 200 | |
            | POST | `/{gadgetId}/ship` † | 204 | |
            | PUT | `/{gadgetId}` | 200 | A route nothing exposes. |

            ### Elsewhere

            | Method | Route | Success | Notes |
            |---|---|---|---|
            | POST | `/api/v1/ungrouped` | 200 | |

            ## Error codes

            | Method | Route | Success | Notes |
            |---|---|---|---|
            | GET | `/api/v1/not-a-route` | 200 | |
            """);

        documented.ShouldBe(
            [
                "GET /api/v1/gadgets",
                "GET /api/v1/gadgets/{gadgetId}",
                "HEAD /api/v1/gadgets/{gadgetId}",
                "POST /api/v1/gadgets/{gadgetId}/ship",
                "PUT /api/v1/gadgets/{gadgetId}",
                "POST /api/v1/ungrouped",
            ],
            ignoreOrder: true);
        exposed.Except(documented).ShouldBe(["DELETE /api/v1/gadgets/{gadgetId}"]);
        documented.Except(exposed).ShouldBe(["PUT /api/v1/gadgets/{gadgetId}"]);
    }

    private static string AddRow(string route)
    {
        string[] parts = route.Split(' ', 2);

        return $"add the row | {parts[0]} | `{parts[1]}` | <success status> | <note> |";
    }

    private static string ApiReference() =>
        File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docs", "reference", "api.md"));

    private static List<Type> ApiControllers() =>
        [.. typeof(IdempotentAttribute).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))];

    private static SortedSet<string> ExposedRoutes(IEnumerable<Type> controllers)
    {
        var routes = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var controller in controllers)
        {
            string prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var verb in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    string template = verb.Template ?? string.Empty;
                    string route = template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal)
                        ? template.TrimStart('~')
                        : $"{prefix}/{template}";

                    foreach (string httpMethod in verb.HttpMethods)
                    {
                        routes.Add($"{httpMethod} {Normalised(route)}");
                    }
                }
            }
        }

        return routes;
    }

    private static string Normalised(string route)
    {
        string versioned = _versionSegment.Replace(route, "v1");
        string unconstrained = _routeConstraint.Replace(versioned, "{${name}}");

        return "/" + unconstrained.Trim('/');
    }

    private static SortedSet<string> DocumentedRoutes(string reference) =>
        new(
            _sectionStart.Split(RoutesSection(reference)).Skip(1).SelectMany(RoutesOfBlock),
            StringComparer.Ordinal);

    private static string RoutesSection(string reference)
    {
        int start = reference.IndexOf(_routesHeading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, "docs/reference/api.md has no '## Routes' section to read.");

        int end = reference.IndexOf("\n## ", start + _routesHeading.Length, StringComparison.Ordinal);

        return end < 0 ? reference[start..] : reference[start..end];
    }

    private static IEnumerable<string> RoutesOfBlock(string block)
    {
        string prefix = _sectionPrefix.Match(block) is { Success: true } under ? under.Groups["prefix"].Value : string.Empty;

        foreach (string[] cells in block.Split('\n').Where(line => line.StartsWith("| ", StringComparison.Ordinal)).Select(line => line.Split('|')))
        {
            string cited = CitedPath(cells);

            if (!cited.StartsWith('/') || cited.StartsWith("/health", StringComparison.Ordinal))
            {
                continue;
            }

            string route = cited.StartsWith("/api/", StringComparison.Ordinal) ? cited : prefix + RelativeToPrefix(cited);

            foreach (string method in cells[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                yield return $"{method} {Normalised(route)}";
            }
        }
    }

    private static string RelativeToPrefix(string cited) => cited == "/" ? string.Empty : cited;

    private static string CitedPath(string[] cells) =>
        cells.Length > 2
            ? cells[2].Replace("†", string.Empty, StringComparison.Ordinal).Trim().Trim('`')
            : string.Empty;

    [Route("api/v{version:apiVersion}/gadgets")]
    private sealed class DeliberateController : ControllerBase
    {
        [HttpGet]
        public OkResult List() => Ok();

        [HttpGet("{gadgetId:guid}")]
        [HttpHead("{gadgetId:guid}")]
        public OkResult Read(Guid gadgetId) => Ok();

        [HttpPost("{gadgetId:guid}/ship")]
        public OkResult Ship(Guid gadgetId) => Ok();

        [HttpDelete("{gadgetId:guid}")]
        public OkResult Remove(Guid gadgetId) => Ok();

        [HttpPost("/api/v1/ungrouped")]
        public OkResult Ungrouped() => Ok();
    }
}
