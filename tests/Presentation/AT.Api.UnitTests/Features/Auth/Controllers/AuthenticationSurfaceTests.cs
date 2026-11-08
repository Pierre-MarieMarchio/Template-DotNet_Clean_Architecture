using System.Reflection;
using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Controllers;

public sealed class AuthenticationSurfaceTests
{
    private static readonly Type[] _controllers =
    [
        typeof(RegistrationController),
        typeof(SessionsController),
        typeof(AccountController),
        typeof(TwoFactorController),
        typeof(PasswordRecoveryController),
        typeof(PasskeysController),
    ];

    [Fact]
    public void EveryEndpointThatMintsATokenPair_IsOnTheCredentialBudget_AndIsNoStore()
    {
        string[] minting =
        [
            nameof(SessionsController.Login),
            nameof(SessionsController.LoginWithExternalProvider),
            nameof(SessionsController.LoginWithTwoFactor),
            nameof(SessionsController.Refresh),
            nameof(PasskeysController.CompletePasskeySignIn),
        ];

        foreach (string name in minting)
        {
            var action = ActionNamed(name);

            action.GetCustomAttribute<EnableRateLimitingAttribute>()
                .ShouldNotBeNull($"{name} answers with credentials and must be rate limited.")
                .PolicyName.ShouldBe(RateLimitingExtensions.Authentication);

            action.GetCustomAttribute<NoStoreAttribute>().ShouldNotBeNull(
                $"RFC 6749 §5.1 forbids any cache from storing {name}'s response.");
        }
    }

    [Fact]
    public void LoginWithExternalProvider_IsTheVisibleExceptionToDefaultDeny()
    {
        var action = ActionNamed(nameof(SessionsController.LoginWithExternalProvider));

        action.GetCustomAttribute<AllowAnonymousAttribute>().ShouldNotBeNull(
            "a caller signing in has no token yet, so the fallback policy has to be opted out of "
            + "here — by name, and listed in HttpSurfaceTests along with every other exception.");

        action.GetCustomAttribute<AuthorizeAttribute>().ShouldBeNull(
            "carrying both resolves to anonymous, which hides the decision rather than declaring it.");
    }

    [Fact]
    public void NoAuthenticationAction_IsIdempotent() => ActionsWith<IdempotentAttribute>().ShouldBeEmpty();

    [Fact]
    public void NoAuthenticationAction_WidensTheInboundBodyLimit()
    {
        string[] offenders =
        [
            .. ActionsWith<RequestSizeLimitAttribute>(),
            .. ActionsWith<DisableRequestSizeLimitAttribute>(),
            .. ActionsWith<RequestFormLimitsAttribute>(),
        ];

        offenders.ShouldBeEmpty();
    }

    private static MethodInfo ActionNamed(string name) =>
        Actions().SingleOrDefault(method => string.Equals(method.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No authentication controller declares an action named '{name}'.");

    private static IReadOnlyList<string> ActionsWith<TAttribute>() where TAttribute : Attribute =>
    [
        .. Actions()
            .Where(method => method.GetCustomAttribute<TAttribute>() is not null)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal),
    ];

    private static List<MethodInfo> Actions()
    {
        var actions = _controllers
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .ToList();

        actions.Count.ShouldBeGreaterThanOrEqualTo(
            28,
            "Far fewer actions were found than the six controllers declare between them, so every "
            + "attribute rule in this class is passing over an empty set.");

        return actions;
    }

}
