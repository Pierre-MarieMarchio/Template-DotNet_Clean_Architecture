using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Auth.UnitTests.TestDoubles;

internal sealed class StubCurrentUser(UserId? userId) : ICurrentUser
{
    public static StubCurrentUser Anonymous { get; } = new(null);

    public static StubCurrentUser WithId(Guid userId) =>
        new(AppTemplate.Domain.Core.Common.Primitives.UserId.Create(userId));

    public UserId? UserId => userId;
}
