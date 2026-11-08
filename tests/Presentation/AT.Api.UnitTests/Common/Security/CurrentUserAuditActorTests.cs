using AppTemplate.Api.Common.Security;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class CurrentUserAuditActorTests
{
    [Fact]
    public void TheActor_RecordsWhoeverIsCalling()
    {
        var caller = Guid.CreateVersion7();

        new CurrentUserAuditActor(new StubCurrentUser(caller)).UserId.ShouldBe(caller);
    }

    [Fact]
    public void AnAnonymousRequest_RecordsNobody() =>
        new CurrentUserAuditActor(new StubCurrentUser(null)).UserId.ShouldBeNull();

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
        public UserId? UserId { get; private set; } = AppTemplate.Domain.Core.Common.Primitives.UserId.CreateOptional(userId);

        internal void Set(Guid? value) => UserId = AppTemplate.Domain.Core.Common.Primitives.UserId.CreateOptional(value);
    }
}
