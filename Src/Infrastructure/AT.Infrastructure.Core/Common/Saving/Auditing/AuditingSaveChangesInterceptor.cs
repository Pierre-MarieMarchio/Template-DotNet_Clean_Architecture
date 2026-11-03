using AppTemplate.Domain.Core.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Auditing;

public sealed class AuditingSaveChangesInterceptor(
    IAuditActor auditActor,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var userId = auditActor.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.SetCreated(now, userId);
                    break;

                case EntityState.Modified:
                    entry.Entity.SetLastModified(now, userId);
                    break;

                default:
                    break;
            }
        }
    }
}
