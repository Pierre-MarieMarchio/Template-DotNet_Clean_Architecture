using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Presentation.Core.Common.Security;
using AppTemplate.Worker.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Common.Security;

/// <summary>
/// The counterpart to <see cref="NoCallerCurrentUser"/>, and the reason the two abstractions are
/// separate: asking this host <em>who is calling</em> is a composition mistake and throws, while
/// asking it <em>whom to record</em> has a truthful answer. One type answering both leaves every
/// background loop unable to commit, because the audit interceptor runs on every save.
/// </summary>
public sealed class BackgroundAuditActorTests
{
    private readonly BackgroundAuditActor _sut = new();

    [Fact]
    public void UserId_IsNobody_RatherThanThrowing()
    {
        Guid? userId = _sut.UserId;

        userId.ShouldBeNull();
    }

    [Fact]
    public void UserId_DoesNotAnswerLikeTheNoCallerIdentity()
    {
        Should.Throw<NotSupportedException>(() => new NoCallerCurrentUser().UserId);

        Should.NotThrow(() => _sut.UserId);
    }

    [Fact]
    public void TheActor_IsNotAlsoTheCurrentUser()
    {
        _sut.ShouldNotBeAssignableTo<ICurrentUser>(
            "one type answering both questions is what made a host with no principal unable to write");
    }

}
