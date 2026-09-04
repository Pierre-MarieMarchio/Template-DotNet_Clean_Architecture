namespace AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;

/// <summary>
/// Who the audit columns name for a write. The host supplies it, the way it supplies
/// <c>ICurrentUser</c> to the application layer — but this is deliberately not that abstraction and
/// deliberately not in that layer, because the application layer never asks this question:
/// <see cref="AuditingSaveChangesInterceptor"/> is the only consumer, and it runs under every save.
/// <para>
/// The distinction is what a host with no request depends on. <c>ICurrentUser</c> answers "who is
/// calling", which such a host cannot answer and must not invent. This one answers "whom do we
/// record", which it can: nobody. Collapsing the two made every commit from <c>AppTemplate.Worker</c>
/// throw at the stamp, so no deposited file was ever inspected and no due reminder was ever marked
/// as sent.
/// </para>
/// </summary>
public interface IAuditActor
{
    /// <summary>
    /// The id to stamp, or <c>null</c> when no user is responsible — an anonymous request, or a
    /// background pass nobody asked for. <c>null</c> is a fact, not a missing value.
    /// </summary>
    Guid? UserId { get; }
}
