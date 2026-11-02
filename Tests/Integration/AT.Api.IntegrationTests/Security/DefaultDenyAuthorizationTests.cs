using System.Net;
//#if (localAuth)
using System.Reflection;
//#endif
using System.Text;
using System.Text.RegularExpressions;
//#if (localAuth)
using AppTemplate.Api.Features.Auth.Controllers;
//#endif
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
//#if (localAuth)
using Microsoft.AspNetCore.Mvc.Routing;
//#endif
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class DefaultDenyAuthorizationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly Guid _someId = Guid.CreateVersion7();

    private static readonly Regex _routeParameter = new(@"\{[^}:=?]+(?<constraints>[^}=?]*)[^}]*\}", RegexOptions.None, TimeSpan.FromSeconds(5));
    //#if (localAuth)

    private static readonly Type[] _authControllers =
    [
        typeof(RegistrationController),
        typeof(SessionsController),
        typeof(AccountController),
        typeof(PasskeysController),
        typeof(TwoFactorController),
        typeof(PasswordRecoveryController),
    ];
    //#endif

    [Fact]
    public async Task EveryEndpointNotMarkedAnonymous_Answers401WithoutATokenAndForAGarbageOne()
    {
        var requests = ProtectedRequests();

        requests.ShouldNotBeEmpty("The endpoint data source read as empty, so nothing below is checked.");
        //#if (signedInRoute)
        requests.ShouldContain(request => request.Path == ProtectedRoute);
        //#endif

        var offenders = new List<string>();

        foreach (var (method, path) in requests)
        {
            foreach (string? token in new[] { null, "not-a-token" })
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);

                if (token is not null)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                }

                if (method is "POST" or "PUT" or "PATCH")
                {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }

                using var response = await CreateClient().SendAsync(request, TestToken);

                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    offenders.Add($"{method} {path} {(token is null ? "without a token" : "with a garbage token")}: {(int)response.StatusCode}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Authorisation is default-deny: an endpoint that does not opt out answers 401 to a caller " +
            "who is not signed in, whatever its route and its policy.");
    }
    //#if (signedInRoute)

    [Fact]
    public async Task AnAuthenticatedCaller_ReachesAProtectedEndpoint()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
    //#endif
    //#if (localAuth)

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

    private static List<MethodInfo> ActionsOf(Type controller) =>
        [.. controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())];
    //#endif

    private List<(string Method, string Path)> ProtectedRequests() =>
        [.. Fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (Method: method, Path: PathOf(endpoint.RoutePattern.RawText ?? string.Empty))))
            .Distinct()
            .OrderBy(request => request.Path, StringComparer.Ordinal)
            .ThenBy(request => request.Method, StringComparer.Ordinal)];

    private static string PathOf(string template) =>
        "/" + _routeParameter.Replace(template.TrimStart('/'), parameter => SampleOf(parameter.Groups["constraints"].Value));

    private static string SampleOf(string constraints) =>
        constraints.Contains(":guid", StringComparison.Ordinal) ? _someId.ToString()
        : constraints.Contains(":apiVersion", StringComparison.Ordinal) ? "1"
        : "sample";
}
