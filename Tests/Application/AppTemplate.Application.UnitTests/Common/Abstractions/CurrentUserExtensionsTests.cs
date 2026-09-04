using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.UnitTests.TestDoubles;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Abstractions;

public sealed class CurrentUserExtensionsTests
{
    [Fact]
    public void RequireUserId_ReturnsTheId_WhenTheCallerIsAuthenticated()
    {
        var id = Guid.CreateVersion7();

        var result = StubCurrentUser.WithId(id).RequireUserId();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(id);
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

    /// <summary>
    /// No real authenticated caller carries an empty id: <c>Result&lt;Guid&gt;.Success</c> only
    /// refuses <c>null</c>, so an empty id would otherwise sail through as a success.
    /// </summary>
    [Fact]
    public void RequireUserId_Fails_WhenTheIdIsEmpty()
    {
        var result = StubCurrentUser.WithId(Guid.Empty).RequireUserId();

        result.IsFailure.ShouldBeTrue();

        var error = result.Error;

        error.ShouldNotBeNull();
        error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public void RequireUserId_Rejects_ANullCurrentUser() =>
        Should.Throw<ArgumentNullException>(() => CurrentUserExtensions.RequireUserId(null!));
}
