//#if (todoLists)
using System.Net;
//#endif
using System.Reflection;
using AppTemplate.Api.Features.Auth.Controllers;
//#if (todoLists)
using AppTemplate.Api.Features.TodoLists.Controllers;
//#endif
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class DefaultDenyAuthorizationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    //#if (todoLists)
    private static readonly Guid _someListId = Guid.CreateVersion7();
    private static readonly Guid _someItemId = Guid.CreateVersion7();

    //#endif
    private static readonly Type[] _authControllers =
    [
        typeof(RegistrationController),
        typeof(SessionsController),
        typeof(AccountController),
        typeof(PasskeysController),
        typeof(TwoFactorController),
        typeof(PasswordRecoveryController),
    ];
    //#if (todoLists)

    private static readonly Type[] _todoListControllers =
    [
        typeof(TodoListsController),
        typeof(TodoItemsController),
        typeof(TodoItemTagsController),
    ];

    public static TheoryData<string, string> TodoListEndpoints => new()
    {
        { "GET", TodoListsRoute },
        { "GET", $"{TodoListsRoute}/tags" },
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

    [Fact]
    public void TheEnumerationCoversEveryActionOnTheController()
    {
        var actions = _todoListControllers.SelectMany(ActionsOf).ToList();

        actions.Count.ShouldBe(
            TodoListEndpoints.Count,
            "A TodoList endpoint was added or removed. Update TodoListEndpoints so it stays a "
            + "complete default-deny guard.");
    }

    [Fact]
    public void NoTodoListEndpoint_OptsOutOfAuthorisation()
    {
        foreach (var controller in _todoListControllers)
        {
            controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
                .ShouldBeEmpty($"{controller.Name} opts out of the fallback policy for every action on it.");

            foreach (var action in ActionsOf(controller))
            {
                action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
                    .ShouldBeEmpty($"{controller.Name}.{action.Name} opts out of the fallback policy.");
            }
        }
    }
    //#endif

    [Fact]
    public void TheAuthenticationEndpoints_OptOutOneByOne()
    {
        foreach (var controller in _authControllers)
        {
            controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
                .ShouldBeEmpty(
                    $"A controller-level [AllowAnonymous] on {controller.Name} silently un-protects "
                    + "every authenticated action on it.");

            foreach (var action in ActionsOf(controller))
            {
                bool anonymous = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
                bool authorised = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

                (anonymous ^ authorised).ShouldBeTrue(
                    $"{controller.Name}.{action.Name} must declare exactly one of [AllowAnonymous] or "
                    + "[Authorize]. Carrying neither leaves it to the fallback policy, where nobody "
                    + "reading the action can tell; carrying both resolves to anonymous.");
            }
        }
    }

    [Fact]
    public void OnlyTheAccountActions_RequireAToken()
    {
        string[] authenticated =
        [
            .. _authControllers
                .SelectMany(ActionsOf)
                .Where(action => action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any())
                .Select(action => action.Name)
                .Order(StringComparer.Ordinal),
        ];

        authenticated.ShouldBe(
            [
                nameof(PasskeysController.BeginPasskeyRegistration),
                nameof(AccountController.ChangePassword),
                nameof(PasskeysController.CompletePasskeyRegistration),
                nameof(AccountController.ConfirmEmailChange),
                nameof(TwoFactorController.ConfirmTwoFactorSetup),
                nameof(AccountController.DeleteMyAccount),
                nameof(TwoFactorController.DisableTwoFactor),
                nameof(AccountController.GetCurrentUser),
                nameof(AccountController.GetMySecurityEvents),
                nameof(PasskeysController.ListPasskeys),
                nameof(SessionsController.ListSessions),
                nameof(SessionsController.LogoutEverywhere),
                nameof(PasskeysController.RemovePasskey),
                nameof(AccountController.RequestEmailChange),
                nameof(SessionsController.RevokeSession),
                nameof(TwoFactorController.SetUpTwoFactor),
            ],
            "An authentication endpoint changed sides. Decide deliberately, then update this list.");
    }

    //#if (todoLists)
    [Fact]
    public async Task AnAuthenticatedCaller_ReachesTheSameEndpoints()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    //#endif
    private static List<MethodInfo> ActionsOf(Type controller) =>
        [.. controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())];
}
