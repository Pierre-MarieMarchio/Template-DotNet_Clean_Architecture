using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class StubCurrentUser(UserId? userId) : ICurrentUser
{
    /// <summary>No id at all, rather than an owner whose id happens to be empty.</summary>
    public static StubCurrentUser Anonymous { get; } = new(null);

    public static StubCurrentUser WithId(UserId userId) => new(userId);

    public UserId? UserId => userId;
}
