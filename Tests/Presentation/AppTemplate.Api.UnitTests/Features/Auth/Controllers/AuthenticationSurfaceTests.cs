using System.Reflection;
using AppTemplate.Api.Core.Common.Caching;
using AppTemplate.Api.Core.Common.Idempotency;
using AppTemplate.Api.Core.Common.Security;
using AppTemplate.Api.Features.Auth.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Controllers;

/// <summary>
/// The attributes every authentication endpoint stands on, whichever of the five controllers under
/// the <c>auth</c> prefix declares it.
/// </summary>
/// <remarks>
/// Read off the five types rather than one, because each rule below is a claim about the surface a
/// caller meets: the credential budget, the caching contract, the absence of idempotency and the
/// inbound body limit are properties of a URL. A rule enumerating one class would have gone on
/// passing while asking nothing of the other four.
/// </remarks>
public sealed class AuthenticationSurfaceTests
{
    /// <summary>The five classes the eighteen authentication actions are divided between.</summary>
    private static readonly Type[] _controllers =
    [
        typeof(RegistrationController),
        typeof(SessionsController),
        typeof(AccountController),
        typeof(TwoFactorController),
        typeof(PasswordRecoveryController),
    ];

    #region The attributes this endpoint stands on

    /// <summary>
    /// A sign-in endpoint outside the credential budget is a hole nothing else closes: account
    /// lockout counts failures per account, and this endpoint has no account until a token verifies.
    /// </summary>
    [Fact]
    public void EveryEndpointThatMintsATokenPair_IsOnTheCredentialBudget_AndIsNoStore()
    {
        string[] minting =
        [
            nameof(SessionsController.Login),
            nameof(SessionsController.LoginWithExternalProvider),
            nameof(SessionsController.LoginWithTwoFactor),
            nameof(SessionsController.Refresh),
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

    /// <summary>
    /// Replaying a sign-in must mint a fresh pair, exactly as <c>POST /auth/login</c> does. Marking it
    /// idempotent would put the issued pair in the idempotency store so a retry could be handed the
    /// same one back — a credential at rest in a table whose purpose is to be read back — which is the
    /// reason <see cref="IdempotentAttribute"/> gives for staying off every authentication endpoint.
    /// </summary>
    [Fact]
    public void NoAuthenticationAction_IsIdempotent() => ActionsWith<IdempotentAttribute>().ShouldBeEmpty();

    /// <summary>
    /// An <c>id_token</c> is a few kilobytes of base64url at its largest, so the 64 KiB inbound cap
    /// covers it many times over. An attribute widening it here would be the first sign somebody had
    /// started posting something other than a token to this endpoint.
    /// </summary>
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

    #endregion
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
            18,
            "Far fewer actions were found than the five controllers declare between them, so every "
            + "attribute rule in this class is passing over an empty set.");

        return actions;
    }

}
