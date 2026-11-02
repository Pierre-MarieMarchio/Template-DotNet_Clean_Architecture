using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;

namespace AppTemplate.Api.Common.Security;

internal sealed class BackgroundAuditActor : IAuditActor
{
    public Guid? UserId => null;
}
