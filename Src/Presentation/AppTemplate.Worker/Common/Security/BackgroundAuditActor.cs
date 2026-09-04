using AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;

namespace AppTemplate.Worker.Common.Security;

/// <summary>
/// The worker's answer to "whom do we record": nobody, and that is a truthful answer rather than an
/// absent one. A sweep that promotes a deposited file or rings a due reminder is not something a
/// user did, so the audit columns say so.
/// <para>
/// This is deliberately not <see cref="BackgroundCurrentUser"/>, which throws. The distinction is
/// the point: a use case asking this host <em>who is calling</em> is composed wrongly and must fail
/// loudly, while the audit stamp on a background write is simply unattributed.
/// </para>
/// </summary>
internal sealed class BackgroundAuditActor : IAuditActor
{
    public Guid? UserId => null;
}
