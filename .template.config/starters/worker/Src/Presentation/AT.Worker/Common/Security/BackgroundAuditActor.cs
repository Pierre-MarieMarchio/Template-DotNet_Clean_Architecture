using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;

namespace AppTemplate.Worker.Common.Security;

internal sealed class BackgroundAuditActor : IAuditActor
{
    public Guid? UserId => null;
}
