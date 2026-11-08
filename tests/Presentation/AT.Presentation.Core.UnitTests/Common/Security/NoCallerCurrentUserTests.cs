using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Presentation.Core.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Security;

public sealed class NoCallerCurrentUserTests
{
    private readonly NoCallerCurrentUser _sut = new();

    [Fact]
    public void UserId_Throws_RatherThanReturningNull()
    {
        var exception = Should.Throw<NotSupportedException>(() => _sut.UserId);

        exception.Message.ShouldContain(nameof(ICurrentUser));
        exception.Message.ShouldContain(nameof(ICurrentUser.UserId));
    }
}
