using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Worker.Common.Security;

/// <summary>
/// The worker's answer to "who is calling": nobody. There is no request and no principal here,
/// unlike the API's <c>CurrentUser</c>, which reads an <c>HttpContext</c> that is merely absent
/// for an anonymous request and returns <c>null</c> for that legitimate case. This type has no
/// legitimate case to return <c>null</c> for, so <see cref="UserId"/> throws instead: a use case
/// that reads it from this host is not "running anonymously", it is composed onto a host that
/// cannot supply what it needs, and that must fail loudly rather than silently proceed as if the
/// caller were anonymous.
/// </summary>
internal sealed class BackgroundCurrentUser : ICurrentUser
{
    public Guid? UserId => throw new NotSupportedException(
        $"AppTemplate.Worker has no current user: it runs with no HTTP request and no principal. " +
        $"A use case that reads {nameof(ICurrentUser)}.{nameof(UserId)} cannot run unmodified " +
        "from this host.");

    public bool IsAuthenticated => false;
}
