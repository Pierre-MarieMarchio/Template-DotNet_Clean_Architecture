using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Controllers;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Features.Auth.UseCases.Commands.DisableTwoFactor;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Logout;
using AppTemplate.Application.Features.Auth.UseCases.Commands.LogoutEverywhere;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Register;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RequestEmailChange;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RequestPasswordReset;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ResendConfirmationEmail;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ResetPassword;
using AppTemplate.Application.Features.Auth.UseCases.Commands.SetUpTwoFactor;
using AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;
using AppTemplate.Application.Features.Auth.UseCases.Commands.VerifyTwoFactor;
using AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Controllers;

/// <summary>
/// The transport decisions the external sign-in endpoint takes, asserted where they are taken.
/// </summary>
/// <remarks>
/// The use case is substituted: which sign-ins are accepted is its own subject, and collapsing every
/// refusal into one error is a guarantee it owns. What is left here is what no other layer can be
/// asked about — that the body a client receives really carries the branch tag, that a second factor
/// travels as a challenge rather than as a token pair, and that the credential budget, the caching
/// rule and the anonymity of the endpoint are the ones this controller declares.
/// <para>
/// The bodies are read from a response stream MVC wrote, never from
/// <c>JsonSerializer.Serialize&lt;TBase&gt;(…)</c>: naming the polymorphic base in the test is what
/// puts the discriminator in the JSON, so a test that does it proves its own arithmetic. The
/// discriminator is only really there because <c>ApiControllerBase</c> sets
/// <see cref="ObjectResult.DeclaredType"/>, and only a run through the output formatter can see that.
/// </para>
/// </remarks>
public sealed class AuthControllerTests
{
    private static readonly DateTimeOffset _accessExpiry = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset _refreshExpiry = new(2026, 1, 9, 3, 4, 5, TimeSpan.Zero);

    private readonly ISignInWithExternalProviderUseCase _signInWithExternalProvider =
        Substitute.For<ISignInWithExternalProviderUseCase>();

    #region What reaches the use case

    /// <summary>
    /// Two positional strings on both records, so swapping them compiles and ships an
    /// <c>id_token</c> as a provider name. The values are distinct for that reason.
    /// </summary>
    [Fact]
    public async Task LoginWithExternalProvider_HandsTheProviderAndTheTokenToTheUseCase()
    {
        SignInWithExternalProviderCommand? captured = null;

        _signInWithExternalProvider
            .ExecuteAsync(
                Arg.Do<SignInWithExternalProviderCommand>(command => captured = command),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<SignInWithExternalProviderOutcome>(AnAuthenticatedOutcome()));

        await AController().LoginWithExternalProvider(
            new SignInWithExternalProviderRequest("the-operators-name-for-a-provider", "the.id.token"),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured.Provider.ShouldBe("the-operators-name-for-a-provider");
        captured.IdToken.ShouldBe("the.id.token");
    }

    #endregion

    #region The body a client actually receives

    [Fact]
    public async Task AnAuthenticatedSignIn_IsServedWithItsStatusTag_AndTheTokensNested()
    {
        var httpContext = AContext();

        _signInWithExternalProvider
            .ExecuteAsync(Arg.Any<SignInWithExternalProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<SignInWithExternalProviderOutcome>(AnAuthenticatedOutcome(accountCreated: true)));

        var action = await AController(httpContext).LoginWithExternalProvider(ARequest(), TestContext.Current.CancellationToken);

        var result = action.Result.ShouldBeOfType<OkObjectResult>();
        result.DeclaredType.ShouldBe(
            typeof(ExternalLoginResponse),
            "the discriminator below is written only because serialisation starts at the polymorphic "
            + "base, which is what DeclaredType decides.");

        using var body = await ServedBodyAsync(result, httpContext);
        var root = body.RootElement;

        root.GetProperty("status").GetString().ShouldBe("authenticated");
        root.GetProperty("accountCreated").GetBoolean().ShouldBeTrue();

        var tokens = root.GetProperty("tokens");
        tokens.GetProperty("accessToken").GetString().ShouldBe("access");
        tokens.GetProperty("accessTokenExpiresAt").GetDateTimeOffset().ShouldBe(_accessExpiry);
        tokens.GetProperty("refreshToken").GetString().ShouldBe("refresh");
        tokens.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset().ShouldBe(_refreshExpiry);

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    /// <summary>
    /// The one that matters most: an account with a second factor armed must get a challenge out of
    /// this endpoint and nothing else. Serving a pair here would make linking a provider the way to
    /// walk around a second factor its owner armed.
    /// </summary>
    [Fact]
    public async Task ASignInIntoATwoFactorAccount_IsServedAsAChallenge_AndCarriesNoToken()
    {
        var httpContext = AContext();

        _signInWithExternalProvider
            .ExecuteAsync(Arg.Any<SignInWithExternalProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<SignInWithExternalProviderOutcome>(
                new SignInWithExternalProviderOutcome.TwoFactorRequired("the-challenge")));

        var action = await AController(httpContext).LoginWithExternalProvider(ARequest(), TestContext.Current.CancellationToken);

        var result = action.Result.ShouldBeOfType<OkObjectResult>();

        using var body = await ServedBodyAsync(result, httpContext);

        body.RootElement.GetProperty("status").GetString().ShouldBe("twoFactorRequired");
        body.RootElement.GetProperty("challengeToken").GetString().ShouldBe("the-challenge");

        PropertyNames(body).ShouldBe(
            ["challengeToken", "status"],
            "a challenge response carries a challenge and nothing else: an access or refresh token "
            + "reaching a caller here is the second factor bypassed.");
    }

    /// <summary>
    /// The branch tags and the field names match <c>POST /auth/login</c>'s, which is the whole reason
    /// this endpoint has a shape of its own rather than a bespoke one: a client already parsing a
    /// password sign-in parses this without a second code path.
    /// </summary>
    [Fact]
    public void TheTwoBranches_AreTaggedTheSameWayLoginsAre()
    {
        DiscriminatorsOf(typeof(ExternalLoginResponse)).ShouldBe(DiscriminatorsOf(typeof(LoginResponse)));
    }

    #endregion

    #region What a refusal says

    [Fact]
    public async Task ARefusal_Answers401_WithTheStableCode()
    {
        var httpContext = AContext();

        _signInWithExternalProvider
            .ExecuteAsync(Arg.Any<SignInWithExternalProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SignInWithExternalProviderOutcome>(AuthErrors.ExternalSignInRefused));

        var action = await AController(httpContext).LoginWithExternalProvider(ARequest(), TestContext.Current.CancellationToken);

        var problem = action.Result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);

        using var body = await ServedBodyAsync(problem, httpContext);

        body.RootElement.GetProperty("code").GetString().ShouldBe("auth.externalSignIn.refused");
        body.RootElement.TryGetProperty("errors", out _).ShouldBeFalse(
            "a per-field error dictionary would name which part of the request was wrong, and every "
            + "part of it is something an attacker chose.");
    }

    /// <summary>
    /// Which cause produced the refusal is the application layer's to hide, and it hides it by
    /// returning one error for all of them. What this asserts is the half the API owns: that nothing
    /// derived from the request reaches the body, so a refusal cannot be read for whether the provider
    /// name meant anything or the token was even the right kind. Only the trace identifier differs,
    /// and it names the request rather than its outcome.
    /// </summary>
    [Fact]
    public async Task EveryRefusal_LooksTheSame_WhateverWasSent()
    {
        _signInWithExternalProvider
            .ExecuteAsync(Arg.Any<SignInWithExternalProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SignInWithExternalProviderOutcome>(AuthErrors.ExternalSignInRefused));

        string configured = await RefusalBodyAsync(new SignInWithExternalProviderRequest("google", "a.forged.token"));
        string unknown = await RefusalBodyAsync(new SignInWithExternalProviderRequest("no-such-provider", "a.forged.token"));
        string unverified = await RefusalBodyAsync(new SignInWithExternalProviderRequest("google", "another.token"));

        unknown.ShouldBe(
            configured,
            "an unknown provider must answer exactly as a configured one refusing does, or probing "
            + "this endpoint enumerates which providers the installation accepts.");

        unverified.ShouldBe(configured);
    }

    #endregion

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
            nameof(AuthController.Login),
            nameof(AuthController.LoginWithExternalProvider),
            nameof(AuthController.LoginWithTwoFactor),
            nameof(AuthController.Refresh),
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
        var action = ActionNamed(nameof(AuthController.LoginWithExternalProvider));

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
    public void NoAuthenticationAction_IsIdempotent()
    {
        ActionsWith<IdempotentAttribute>().ShouldBeEmpty();
    }

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

    #region Fixture

    private static SignInWithExternalProviderRequest ARequest() => new("a-provider", "an.id.token");

    private static SignInWithExternalProviderOutcome.Authenticated AnAuthenticatedOutcome(bool accountCreated = false) =>
        new(
            UserId: Guid.CreateVersion7(),
            UserName: "ada",
            Email: "ada@example.com",
            AccessToken: "access",
            AccessTokenExpiresAt: _accessExpiry,
            RefreshToken: "refresh",
            RefreshTokenExpiresAt: _refreshExpiry,
            AccountCreated: accountCreated);

    private async Task<string> RefusalBodyAsync(SignInWithExternalProviderRequest request)
    {
        var httpContext = AContext();

        var action = await AController(httpContext).LoginWithExternalProvider(request, TestContext.Current.CancellationToken);

        using var body = await ServedBodyAsync(action.Result.ShouldBeOfType<ObjectResult>(), httpContext);

        // Everything except the trace identifier, which names the request rather than its outcome, so
        // any two responses differ in it and comparing whole documents would assert that two requests
        // are the same one.
        var fields = body.RootElement
            .EnumerateObject()
            .Where(property => !string.Equals(property.Name, "traceId", StringComparison.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value.GetRawText()}");

        return string.Join('\n', fields);
    }

    /// <summary>
    /// The body MVC wrote, obtained by running the result through the same
    /// <c>IActionResultExecutor</c> and output formatter the pipeline uses.
    /// </summary>
    private static async Task<JsonDocument> ServedBodyAsync(ObjectResult result, HttpContext httpContext)
    {
        using var written = new MemoryStream();
        httpContext.Response.Body = written;

        await result.ExecuteResultAsync(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));

        written.Position = 0;

        return await JsonDocument.ParseAsync(written, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static IReadOnlyList<string> PropertyNames(JsonDocument body) =>
    [
        .. body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal),
    ];

    private static IReadOnlyList<string> DiscriminatorsOf(Type response) =>
    [
        .. response.GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.TypeDiscriminator?.ToString() ?? attribute.DerivedType.Name)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Carries what MVC's own object-result executor resolves — the formatter set, the response
    /// stream writer factory, the logger — so the JSON asserted on is the JSON the pipeline produces.
    /// </summary>
    private static DefaultHttpContext AContext()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddControllers();
        services.AddSingleton(Options.Create(new ProblemTypeOptions { BaseUri = ProblemTypes.DefaultBaseUri }));

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            TraceIdentifier = $"trace-{Guid.NewGuid():N}",
        };
    }

    private AuthController AController(HttpContext? httpContext = null) =>
        new(
            Substitute.For<IRegisterUseCase>(),
            Substitute.For<ILoginUseCase>(),
            Substitute.For<IRefreshAccessTokenUseCase>(),
            Substitute.For<IConfirmEmailUseCase>(),
            Substitute.For<IResendConfirmationEmailUseCase>(),
            Substitute.For<ILogoutUseCase>(),
            Substitute.For<ILogoutEverywhereUseCase>(),
            Substitute.For<IGetCurrentUserUseCase>(),
            Substitute.For<IChangePasswordUseCase>(),
            Substitute.For<IRequestEmailChangeUseCase>(),
            Substitute.For<IConfirmEmailChangeUseCase>(),
            Substitute.For<IRequestPasswordResetUseCase>(),
            Substitute.For<IResetPasswordUseCase>(),
            Substitute.For<ISetUpTwoFactorUseCase>(),
            Substitute.For<IConfirmTwoFactorSetupUseCase>(),
            Substitute.For<IDisableTwoFactorUseCase>(),
            Substitute.For<IVerifyTwoFactorUseCase>(),
            _signInWithExternalProvider)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext ?? AContext() },
        };

    private static MethodInfo ActionNamed(string name) =>
        Actions().SingleOrDefault(method => string.Equals(method.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"{nameof(AuthController)} declares no action named '{name}'.");

    private static IReadOnlyList<string> ActionsWith<TAttribute>() where TAttribute : Attribute =>
    [
        .. Actions()
            .Where(method => method.GetCustomAttribute<TAttribute>() is not null)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal),
    ];

    private static List<MethodInfo> Actions()
    {
        var actions = typeof(AuthController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToList();

        actions.Count.ShouldBeGreaterThanOrEqualTo(
            18,
            "Far fewer actions were found than this controller declares, so every attribute rule in "
            + "this class is passing over an empty set.");

        return actions;
    }

    #endregion
}
