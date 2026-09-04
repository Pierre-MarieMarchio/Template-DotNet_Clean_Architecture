using AppTemplate.Api.Common.Security;
using AppTemplate.Application.Common.Ports;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

/// <summary>
/// In this host the two questions have one answer, and the point of the type is that it delegates
/// rather than reading the principal a second time: two places turning a claim into a user id is
/// two places that can disagree about what an anonymous request means.
/// </summary>
public sealed class CurrentUserAuditActorTests
{
    [Fact]
    public void TheActor_RecordsWhoeverIsCalling()
    {
        var caller = Guid.CreateVersion7();

        new CurrentUserAuditActor(new StubCurrentUser(caller)).UserId.ShouldBe(caller);
    }

    /// <summary>
    /// An anonymous request records nobody — the same answer the worker gives, and a fact worth
    /// keeping rather than a missing value.
    /// </summary>
    [Fact]
    public void AnAnonymousRequest_RecordsNobody() =>
        new CurrentUserAuditActor(new StubCurrentUser(null)).UserId.ShouldBeNull();

    /// <summary>
    /// Read per access, not captured: the audit stamp is taken during a save, which is after
    /// authentication has run, and a value cached at construction would stamp the wrong caller for
    /// the rest of a request that authenticated late.
    /// </summary>
    [Fact]
    public void TheActor_ReadsThroughOnEveryAccess()
    {
        var user = new StubCurrentUser(null);
        var actor = new CurrentUserAuditActor(user);

        actor.UserId.ShouldBeNull();

        var caller = Guid.CreateVersion7();
        user.Set(caller);

        actor.UserId.ShouldBe(caller, "the actor must not cache what the principal said first");
    }

    private sealed class StubCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; private set; } = userId;

        internal void Set(Guid? value) => UserId = value;
    }
}
