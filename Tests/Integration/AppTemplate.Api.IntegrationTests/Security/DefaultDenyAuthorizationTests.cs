using System.Net;
using System.Reflection;
using AppTemplate.Api.Features.Auth.Controllers;
using AppTemplate.Api.Features.TodoLists.Controllers;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// Nothing on the TodoList aggregate is reachable without a token.
/// </summary>
/// <remarks>
/// Authorisation comes from the host's fallback policy rather than from each action, so the guard has
/// to cover two things: that every endpoint answers 401, and that a <em>new</em> endpoint cannot
/// quietly appear outside the enumeration below.
/// </remarks>
public sealed class DefaultDenyAuthorizationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly Guid _someListId = Guid.CreateVersion7();
    private static readonly Guid _someItemId = Guid.CreateVersion7();

    public static TheoryData<string, string> TodoListEndpoints => new()
    {
        { "GET", TodoListsRoute },
        { "GET", $"{TodoListsRoute}/{_someListId}" },
        { "GET", $"{TodoListsRoute}/{_someListId}/items" },
        { "GET", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}" },
        { "POST", TodoListsRoute },
        { "PUT", $"{TodoListsRoute}/{_someListId}" },
        { "DELETE", $"{TodoListsRoute}/{_someListId}" },
        { "POST", $"{TodoListsRoute}/{_someListId}/items" },
        { "PUT", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}" },
        { "POST", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}/complete" },
        { "POST", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}/reopen" },
        { "DELETE", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}" },
        { "POST", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}/tags" },
        { "PUT", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}/tags" },
        { "DELETE", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}/tags/urgent" },
    };

    [Theory]
    [MemberData(nameof(TodoListEndpoints))]
    public async Task EveryTodoListEndpoint_Answers401WithoutAToken(string method, string path)
    {
        var client = CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            // A body on every verb that takes one, so a 401 cannot be confused with a 415 or a 400
            // raised before authorisation ran.
            Content = method is "POST" or "PUT"
                ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                : null,
        };

        using var response = await client.SendAsync(request, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(TodoListEndpoints))]
    public async Task EveryTodoListEndpoint_Answers401ForAGarbageToken(string method, string path)
    {
        var client = CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer not-a-token");

        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The enumeration above is only a regression guard if it is complete. This fails the moment an
    /// action is added to the controller, which is exactly when somebody needs to be reminded to
    /// cover it.
    /// </summary>
    [Fact]
    public void TheEnumerationCoversEveryActionOnTheController()
    {
        var actions = ActionsOf(typeof(TodoListsController));

        actions.Count.ShouldBe(
            TodoListEndpoints.Count,
            "A TodoList endpoint was added or removed. Update TodoListEndpoints so it stays a "
            + "complete default-deny guard.");
    }

    /// <summary>
    /// Default-deny means an exemption is a visible decision. No action on the aggregate's controller
    /// may opt out, at either the action or the controller level.
    /// </summary>
    [Fact]
    public void NoTodoListEndpoint_OptsOutOfAuthorisation()
    {
        typeof(TodoListsController)
            .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
            .ShouldBeEmpty();

        foreach (var action in ActionsOf(typeof(TodoListsController)))
        {
            action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
                .ShouldBeEmpty($"{action.Name} opts out of the fallback policy.");
        }
    }

    /// <summary>
    /// The other half of the same rule: the endpoints that <em>must</em> be anonymous say so, rather
    /// than being anonymous because nothing protected them.
    /// </summary>
    /// <remarks>
    /// The exemption is declared per action and never on the controller. Authorisation is skipped for
    /// an endpoint as soon as <c>IAllowAnonymous</c> appears <em>anywhere</em> in its metadata, so a
    /// class-level <c>[AllowAnonymous]</c> would defeat the <c>[Authorize]</c> on the two actions
    /// below and serve the caller's own profile to anyone — which is why its absence is asserted here
    /// rather than its presence.
    /// </remarks>
    [Fact]
    public void TheAuthenticationEndpoints_OptOutOneByOne()
    {
        typeof(AuthController)
            .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
            .ShouldBeEmpty(
                "A controller-level [AllowAnonymous] on AuthController silently un-protects every "
                + "authenticated action on it.");

        foreach (var action in ActionsOf(typeof(AuthController)))
        {
            bool anonymous = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
            bool authorised = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

            (anonymous ^ authorised).ShouldBeTrue(
                $"{action.Name} must declare exactly one of [AllowAnonymous] or [Authorize]. Carrying "
                + "neither leaves it to the fallback policy, where nobody reading the action can tell; "
                + "carrying both resolves to anonymous.");
        }
    }

    /// <summary>
    /// Which authentication actions require a token, enumerated so that adding one without deciding
    /// fails here. Everything else on the controller is anonymous by necessity: a caller signing in,
    /// confirming an address or resetting a password has no token yet.
    /// </summary>
    [Fact]
    public void OnlyTheAccountActions_RequireAToken()
    {
        string[] authenticated =
        [
            .. ActionsOf(typeof(AuthController))
                .Where(action => action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any())
                .Select(action => action.Name)
                .Order(StringComparer.Ordinal),
        ];

        authenticated.ShouldBe(
            [
                nameof(AuthController.ChangePassword),
                nameof(AuthController.GetCurrentUser),
                nameof(AuthController.LogoutEverywhere),
            ],
            "An authentication endpoint changed sides. Decide deliberately, then update this list.");
    }

    [Fact]
    public async Task AnAuthenticatedCaller_ReachesTheSameEndpoints()
    {
        // The complement of the 401 theory: it proves those endpoints exist and are routable, so a
        // 401 above cannot have been a 404 in disguise.
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static List<MethodInfo> ActionsOf(Type controller) =>
        [.. controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())];
}
