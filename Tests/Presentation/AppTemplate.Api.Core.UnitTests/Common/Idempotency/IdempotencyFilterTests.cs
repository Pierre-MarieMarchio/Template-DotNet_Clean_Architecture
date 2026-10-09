using AppTemplate.Api.Core.Common.Idempotency;
using AppTemplate.Api.Core.UnitTests.TestSupport;
using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

namespace AppTemplate.Api.Core.UnitTests.Common.Idempotency;

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
        currentUser.UserId.Returns((UserId?)null);
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
        currentUser.UserId.Returns((UserId?)null);

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
        currentUser.UserId.Returns((UserId?)null);

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

    /// <summary>
    /// Response writing starts only once the action has already produced a result, and every
    /// <see cref="IdempotentAttribute"/> action returns one only once its use case has committed. An
    /// exception caught with the response already under way must therefore never release the claim —
    /// doing so would let a retry run an already-committed write again for real. Removing the
    /// <c>HasStarted</c> guard in <see cref="IdempotencyFilter"/> turns this red: without it, every
    /// exception out of <c>next()</c> releases the claim, this scenario included.
    /// </summary>
    [Fact]
    public async Task AnExceptionAfterTheResponseHasStarted_DoesNotReleaseTheClaim()
    {
        var store = Substitute.For<IIdempotencyStore>();
        store.ClaimAsync(
            Arg.Any<IdempotencyKey>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(IdempotencyClaim.Claimed());

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var filter = new IdempotencyFilter(
            store,
            Options.Create(new IdempotencyOptions()),
            currentUser,
            Substitute.For<IDateTimeProvider>(),
            new RecordingLogger<IdempotencyFilter>());

        var context = CreateIdempotentPostContext(idempotencyKey: "some-key");
        context.HttpContext.Features.Set<IHttpResponseFeature>(new AlreadyStartedResponseFeature());

        ResourceExecutionDelegate next = () => throw new InvalidOperationException("client disconnected mid-write");

        await Should.ThrowAsync<InvalidOperationException>(() => filter.OnResourceExecutionAsync(context, next));

        await store.DidNotReceive().ReleaseAsync(Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The counterpart to the test above: nothing has been written yet, so whatever threw did so
    /// before the action could have produced a committed result at all — releasing here is still
    /// exactly right, and must stay right after the fix above.
    /// </summary>
    [Fact]
    public async Task AnExceptionBeforeTheResponseHasStarted_StillReleasesTheClaim()
    {
        var store = Substitute.For<IIdempotencyStore>();
        store.ClaimAsync(
            Arg.Any<IdempotencyKey>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(IdempotencyClaim.Claimed());

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var filter = new IdempotencyFilter(
            store,
            Options.Create(new IdempotencyOptions()),
            currentUser,
            Substitute.For<IDateTimeProvider>(),
            new RecordingLogger<IdempotencyFilter>());

        var context = CreateIdempotentPostContext(idempotencyKey: "some-key");

        ResourceExecutionDelegate next = () => throw new InvalidOperationException("the use case blew up before committing anything");

        await Should.ThrowAsync<InvalidOperationException>(() => filter.OnResourceExecutionAsync(context, next));

        await store.Received(1).ReleaseAsync(Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A 204 is a <see cref="StatusCodeResult"/>, not an <see cref="ObjectResult"/>: completing its claim
    /// is what lets a retry be answered instead of running the write again.
    /// </summary>
    [Fact]
    public async Task ASuccessful204_CompletesTheClaim_WithItsStatusAndNoBody()
    {
        var store = AStoreThatClaims();
        var filter = AFilterOver(store);
        var context = CreateIdempotentPostContext(idempotencyKey: "some-key");

        await filter.OnResourceExecutionAsync(context, () => Task.FromResult(
            new ResourceExecutedContext(context, filters: []) { Result = new NoContentResult() }));

        await store.Received(1).CompleteAsync(
            Arg.Any<IdempotencyKey>(),
            Arg.Is<IdempotentResponse>(response => response.StatusCode == StatusCodes.Status204NoContent && response.Body == null),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().ReleaseAsync(Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARepeated204_IsAnsweredWithTheReplayMarker_AndTheActionDoesNotRunAgain()
    {
        var store = Substitute.For<IIdempotencyStore>();
        store.ClaimAsync(
            Arg.Any<IdempotencyKey>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(IdempotencyClaim.Replay(
                new IdempotentResponse(StatusCodes.Status204NoContent, null, "/api/v1/things/1", "\"v1\"")));

        var filter = AFilterOver(store);
        var context = CreateIdempotentPostContext(idempotencyKey: "some-key");
        bool nextWasCalled = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            nextWasCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, filters: []));
        });

        nextWasCalled.ShouldBeFalse();
        context.Result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status204NoContent);

        var headers = context.HttpContext.Response.Headers;
        headers["Idempotency-Replayed"].ToString().ShouldBe("true");
        headers.Location.ToString().ShouldBe("/api/v1/things/1");
        headers.ETag.ToString().ShouldBe("\"v1\"");
    }

    private static IIdempotencyStore AStoreThatClaims()
    {
        var store = Substitute.For<IIdempotencyStore>();
        store.ClaimAsync(
            Arg.Any<IdempotencyKey>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(IdempotencyClaim.Claimed());

        return store;
    }

    private static IdempotencyFilter AFilterOver(IIdempotencyStore store)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        return new IdempotencyFilter(
            store,
            Options.Create(new IdempotencyOptions()),
            currentUser,
            Substitute.For<IDateTimeProvider>(),
            new RecordingLogger<IdempotencyFilter>());
    }

    /// <summary>A response that has already started sending, the way a real Kestrel one would report it.</summary>
    private sealed class AlreadyStartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
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
