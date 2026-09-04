using AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;

namespace AppTemplate.Infrastructure.Identity.IntegrationTests.Fixtures;

/// <summary>
/// The one adapter the host supplies rather than a module. Auditing needs it to resolve a context at
/// all, and nothing under test writes a row anybody is responsible for.
/// </summary>
internal sealed class AnonymousAuditActor : IAuditActor
{
    public Guid? UserId => null;
}
