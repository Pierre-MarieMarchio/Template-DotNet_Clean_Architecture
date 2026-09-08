using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Ports;

public sealed class CurrentUserExtensionsTests
{
    private sealed class StubCurrentUser(UserId? userId) : ICurrentUser
    {
        /// <summary>No id at all, rather than an owner whose id happens to be empty.</summary>
        public static StubCurrentUser Anonymous { get; } = new(null);

        /// <summary>
        /// The factory call is qualified because this class has a member of the same name.
        /// </summary>
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

    /// <summary>
    /// The only failure this narrowing has. An empty id is not a second one: <see cref="UserId"/>
    /// refuses one at construction, so the port has none to answer with —
    /// <c>UserIdTests.Create_Rejects_AnEmptyIdentifier</c> holds that, and
    /// <c>CreateOptional_AnswersNull_ForAnEmptyIdentifier</c> is what makes an empty claim read as
    /// an anonymous request rather than as an owner.
    /// </summary>
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
