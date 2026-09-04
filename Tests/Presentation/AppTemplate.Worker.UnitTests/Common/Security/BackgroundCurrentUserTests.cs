using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Worker.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Common.Security;

/// <summary>
/// The whole point of this adapter: a use case that reads <see cref="ICurrentUser.UserId"/> from
/// the worker must fail loudly, not receive the same <c>null</c> an anonymous HTTP request would
/// get from the API's own <c>CurrentUser</c>.
/// </summary>
public sealed class BackgroundCurrentUserTests
{
    private readonly BackgroundCurrentUser _sut = new();

    [Fact]
    public void UserId_Throws_RatherThanReturningNull()
    {
        var exception = Should.Throw<NotSupportedException>(() => _sut.UserId);

        exception.Message.ShouldContain("AppTemplate.Worker");
    }

}
