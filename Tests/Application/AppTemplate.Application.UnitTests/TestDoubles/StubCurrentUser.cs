using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class StubCurrentUser(UserId? userId) : ICurrentUser
{
    public static StubCurrentUser Anonymous { get; } = new(null);

    public static StubCurrentUser WithId(UserId userId) => new(userId);

    public UserId? UserId => userId;
}
