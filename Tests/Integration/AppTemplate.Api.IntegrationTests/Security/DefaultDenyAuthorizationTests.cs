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
        { "GET", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}" },
        { "POST", TodoListsRoute },
        { "PUT", $"{TodoListsRoute}/{_someListId}" },
        { "DELETE", $"{TodoListsRoute}/{_someListId}" },
        { "POST", $"{TodoListsRoute}/{_someListId}/items" },
        { "POST", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}/complete" },
        { "DELETE", $"{TodoListsRoute}/{_someListId}/items/{_someItemId}" },
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
    /// The enumeration above is only a regression guard if it is complete. This fails the moment a
    /// ninth action is added to the controller, which is exactly when somebody needs to be reminded
    /// to cover it.
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
    [Fact]
    public void TheAuthenticationEndpoints_OptOutExplicitly()
    {
        typeof(AuthController)
            .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
            .ShouldNotBeEmpty();
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
