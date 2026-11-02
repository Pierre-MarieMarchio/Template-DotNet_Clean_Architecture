using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

internal sealed class AnonymousAuditActor : IAuditActor
{
    public Guid? UserId => null;
}
