namespace AppTemplate.Infrastructure.Core.Common.Saving.Auditing;

public interface IAuditActor
{
    Guid? UserId { get; }
}
