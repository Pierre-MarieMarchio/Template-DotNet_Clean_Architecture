using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Ports;

public sealed class CurrentUserExtensionsTests
{
    private sealed class StubCurrentUser(UserId? userId) : ICurrentUser
    {
        public static StubCurrentUser Anonymous { get; } = new(null);

        public static StubCurrentUser WithId(Guid userId) =>
            new(AppTemplate.Domain.Core.Common.Primitives.UserId.Create(userId));

        public UserId? UserId => userId;
    }

    [Fact]
    public void RequireUserId_ReturnsTheId_WhenTheCallerIsAuthenticated()
    {
        var id = Guid.CreateVersion7();

        var result = StubCurrentUser.WithId(id).RequireUserId();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(id);
    }

    [Fact]
    public void RequireUserId_Fails_WhenTheCallerIsAnonymous()
    {
        var result = StubCurrentUser.Anonymous.RequireUserId();

        result.IsFailure.ShouldBeTrue();

        var error = result.Error;

        error.ShouldNotBeNull();
        error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public void RequireUserId_Rejects_ANullCurrentUser() =>
        Should.Throw<ArgumentNullException>(() => CurrentUserExtensions.RequireUserId(null!));
}
