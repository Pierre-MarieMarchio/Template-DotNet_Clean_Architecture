using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class StubCurrentUser(Guid? userId) : ICurrentUser
{
    /// <summary>No id at all, rather than <c>Guid.Empty</c>.</summary>
    public static StubCurrentUser Anonymous { get; } = new(null);

    public static StubCurrentUser WithId(Guid userId) => new(userId);

    public Guid? UserId => userId;
}
