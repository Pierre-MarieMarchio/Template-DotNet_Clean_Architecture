using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Presentation.Core.Common.Security;

public sealed class NoCallerCurrentUser : ICurrentUser
{
    public UserId? UserId => throw new NotSupportedException(
        "This host has no current user: it runs with no request and no principal. A use case that " +
        $"reads {nameof(ICurrentUser)}.{nameof(UserId)} cannot run unmodified from here.");
}
