using AppTemplate.Application.Core.Common.Ports;

namespace AppTemplate.Presentation.Core.Common.Security;

/// <summary>
/// The answer a host with no caller gives to "who is calling": nobody — and it refuses the question
/// rather than answering it.
/// <para>
/// An HTTP host reads a principal that is merely <em>absent</em> for an anonymous request, so
/// <c>null</c> is a legitimate answer there. A background process has no legitimate case to return
/// <c>null</c> for, so <see cref="UserId"/> throws: a use case that reads it here is not "running
/// anonymously", it is composed onto a host that cannot supply what it needs. That has to fail
/// loudly rather than proceed as though the caller were anonymous and quietly widen what the use
/// case is allowed to see.
/// </para>
/// </summary>
public sealed class NoCallerCurrentUser : ICurrentUser
{
    /// <summary>Always throws. There is no caller to identify.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Guid? UserId => throw new NotSupportedException(
        "This host has no current user: it runs with no request and no principal. A use case that " +
        $"reads {nameof(ICurrentUser)}.{nameof(UserId)} cannot run unmodified from here.");

}
