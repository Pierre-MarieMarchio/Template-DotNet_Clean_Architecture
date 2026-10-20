using AppTemplate.Api.Common.Security;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Presentation.Core.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

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
