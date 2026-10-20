using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Controllers;
using AppTemplate.Application.Auth.Features.ExternalLogins.Model.Errors;
using AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.LogoutEverywhere;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.VerifyTwoFactor;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Controllers;

public sealed class SessionsControllerTests
{
    private static readonly DateTimeOffset _accessExpiry = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset _refreshExpiry = new(2026, 1, 9, 3, 4, 5, TimeSpan.Zero);

    private readonly ISignInWithExternalProviderUseCase _signInWithExternalProvider =
        Substitute.For<ISignInWithExternalProviderUseCase>();

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

    [Fact]
    public void TheTwoBranches_AreTaggedTheSameWayLoginsAre() =>
        DiscriminatorsOf(typeof(ExternalLoginResponse)).ShouldBe(DiscriminatorsOf(typeof(LoginResponse)));

    [Fact]
    public async Task ARefusal_Answers401_WithTheStableCode()
    {
        var httpContext = AContext();

        _signInWithExternalProvider
            .ExecuteAsync(Arg.Any<SignInWithExternalProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SignInWithExternalProviderOutcome>(ExternalLoginsErrors.ExternalSignInRefused));

        var action = await AController(httpContext).LoginWithExternalProvider(ARequest(), TestContext.Current.CancellationToken);

        var problem = action.Result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);

        using var body = await ServedBodyAsync(problem, httpContext);

        body.RootElement.GetProperty("code").GetString().ShouldBe("auth.externalSignIn.refused");
        body.RootElement.TryGetProperty("errors", out _).ShouldBeFalse(
            "a per-field error dictionary would name which part of the request was wrong, and every "
            + "part of it is something an attacker chose.");
    }

    [Fact]
    public async Task EveryRefusal_LooksTheSame_WhateverWasSent()
    {
        _signInWithExternalProvider
            .ExecuteAsync(Arg.Any<SignInWithExternalProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SignInWithExternalProviderOutcome>(ExternalLoginsErrors.ExternalSignInRefused));

        string configured = await RefusalBodyAsync(new SignInWithExternalProviderRequest("google", "a.forged.token"));
        string unknown = await RefusalBodyAsync(new SignInWithExternalProviderRequest("no-such-provider", "a.forged.token"));
        string unverified = await RefusalBodyAsync(new SignInWithExternalProviderRequest("google", "another.token"));

        unknown.ShouldBe(
            configured,
            "an unknown provider must answer exactly as a configured one refusing does, or probing "
            + "this endpoint enumerates which providers the installation accepts.");

        unverified.ShouldBe(configured);
    }

    private static SignInWithExternalProviderRequest ARequest() => new("a-provider", "an.id.token");

    private static SignInWithExternalProviderOutcome.Authenticated AnAuthenticatedOutcome(bool accountCreated = false) =>
        new(
            UserId: Guid.CreateVersion7(),
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

        var fields = body.RootElement
            .EnumerateObject()
            .Where(property => !string.Equals(property.Name, "traceId", StringComparison.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value.GetRawText()}");

        return string.Join('\n', fields);
    }

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

    private static DefaultHttpContext AContext()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddControllers();
        services.AddSingleton(Options.Create(new ProblemTypeOptions()));

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            TraceIdentifier = $"trace-{Guid.NewGuid():N}",
        };
    }

    private SessionsController AController(HttpContext? httpContext = null) =>
        new(
            Substitute.For<ILoginUseCase>(),
            Substitute.For<IVerifyTwoFactorUseCase>(),
            _signInWithExternalProvider,
            Substitute.For<IRefreshAccessTokenUseCase>(),
            Substitute.For<ILogoutUseCase>(),
            Substitute.For<ILogoutEverywhereUseCase>(),
            Substitute.For<IListSessionsUseCase>(),
            Substitute.For<IRevokeSessionUseCase>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext ?? AContext() },
        };
}
