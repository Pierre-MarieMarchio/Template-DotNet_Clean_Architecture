using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Auth.UnitTests.TestDoubles;

internal sealed class StubCurrentUser(UserId? userId) : ICurrentUser
{
    /// <summary>No id at all, rather than <c>Guid.Empty</c>.</summary>
    public static StubCurrentUser Anonymous { get; } = new(null);

    /// <summary>
    /// Takes the raw identifier a test already holds. The factory call is qualified because this
    /// class has a member of the same name.
    /// </summary>
    public static StubCurrentUser WithId(Guid userId) =>
        new(AppTemplate.Domain.Core.Common.Primitives.UserId.Create(userId));

    public UserId? UserId => userId;
}
