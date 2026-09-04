using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.UnitTests.TestSupport;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Idempotency;

/// <summary>
/// A caller <see cref="ICurrentUser"/> cannot resolve to a <see cref="Guid"/> is exactly the profile
/// an <c>Idempotency-Key</c> matters most for — one that replays by design. These tests are what
/// stands behind refusing that caller instead of quietly letting the write through unprotected.
/// </summary>
public sealed class IdempotencyFilterTests
{
    [Fact]
    public async Task ACallerWithNoIdentity_IsRefused_TheStoreIsNeverClaimed_AndAWarningIsLogged()
    {
        var store = Substitute.For<IIdempotencyStore>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var logger = new RecordingLogger<IdempotencyFilter>();

        var filter = new IdempotencyFilter(
            store,
            Options.Create(new IdempotencyOptions()),
            currentUser,
            Substitute.For<IDateTimeProvider>(),
            logger);

        var context = CreateIdempotentPostContext(idempotencyKey: "some-key");
        bool nextWasCalled = false;

        ResourceExecutionDelegate next = () =>
        {
            nextWasCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, filters: []));
        };

        await filter.OnResourceExecutionAsync(context, next);

        nextWasCalled.ShouldBeFalse("a caller this server cannot scope a key to must never reach the action.");

        await store.DidNotReceive().ClaimAsync(
            Arg.Any<IdempotencyKey>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());

        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);

        var problem = result.Value.ShouldBeOfType<ProblemDetails>();
        problem.Extensions["code"].ShouldBe("idempotency.callerNotIdentifiable");

        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
    }

    /// <summary>
    /// The capability stays opt-in: a caller that never asked for it is never refused for something
    /// it did not request, no matter what <see cref="ICurrentUser"/> says about it.
    /// </summary>
    [Fact]
    public async Task ACallerWithNoIdentity_ButNoIdempotencyKeyHeader_ProceedsNormally()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var filter = new IdempotencyFilter(
            Substitute.For<IIdempotencyStore>(),
            Options.Create(new IdempotencyOptions()),
            currentUser,
            Substitute.For<IDateTimeProvider>(),
            new RecordingLogger<IdempotencyFilter>());

        var context = CreateIdempotentPostContext(idempotencyKey: null);
        bool nextWasCalled = false;

        ResourceExecutionDelegate next = () =>
        {
            nextWasCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, filters: []));
        };

        await filter.OnResourceExecutionAsync(context, next);

        nextWasCalled.ShouldBeTrue();
        context.Result.ShouldBeNull();
    }

    /// <summary>A deployment that turns the whole capability off must not gain a new way to refuse.</summary>
    [Fact]
    public async Task WhenIdempotencyIsDisabled_ACallerWithNoIdentity_StillProceedsNormally()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var filter = new IdempotencyFilter(
            Substitute.For<IIdempotencyStore>(),
            Options.Create(new IdempotencyOptions { Enabled = false }),
            currentUser,
            Substitute.For<IDateTimeProvider>(),
            new RecordingLogger<IdempotencyFilter>());

        var context = CreateIdempotentPostContext(idempotencyKey: "some-key");
        bool nextWasCalled = false;

        ResourceExecutionDelegate next = () =>
        {
            nextWasCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, filters: []));
        };

        await filter.OnResourceExecutionAsync(context, next);

        nextWasCalled.ShouldBeTrue();
        context.Result.ShouldBeNull();
    }

    private static ResourceExecutingContext CreateIdempotentPostContext(string? idempotencyKey)
    {
        var httpContext = HttpContextFactory.Create();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/api/v1/todo-lists";

        if (idempotencyKey is not null)
        {
            httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        var actionDescriptor = new ActionDescriptor { EndpointMetadata = [new IdempotentAttribute()] };
        var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);

        return new ResourceExecutingContext(
            actionContext,
            filters: [],
            valueProviderFactories: Array.Empty<IValueProviderFactory>());
    }
}
