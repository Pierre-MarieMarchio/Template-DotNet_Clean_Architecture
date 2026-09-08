using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Presentation.Core.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Security;

/// <summary>
/// The whole point of this adapter: a use case that reads <see cref="ICurrentUser.UserId"/> on a
/// host with no caller must fail loudly, not receive the same <c>null</c> an anonymous HTTP request
/// legitimately gets.
/// </summary>
public sealed class NoCallerCurrentUserTests
{
    private readonly NoCallerCurrentUser _sut = new();

    [Fact]
    public void UserId_Throws_RatherThanReturningNull()
    {
        var exception = Should.Throw<NotSupportedException>(() => _sut.UserId);

        // The message has to name what was read, because the composition mistake it reports is
        // several frames away from wherever it surfaces.
        exception.Message.ShouldContain(nameof(ICurrentUser));
        exception.Message.ShouldContain(nameof(ICurrentUser.UserId));
    }
}
