using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Auditing;

/// <summary>Stamps audit columns on the persistence models that opt in via
/// <see cref="IAuditable"/>. An unattributed write is recorded as <c>null</c> — "we do not know who
/// did this" is a fact worth keeping, where <c>Guid.Empty</c> is a lie that looks like a user id. It
/// takes <see cref="IAuditActor"/> rather than <c>ICurrentUser</c>: this runs on every save, from
/// hosts with no caller to name. It stamps the record, not the aggregate — the tracked entities are
/// the persistence models, so this interceptor is their single writer; a mapper copying audit
/// values out of a domain object would be a second writer, and the two would diverge. It knows no
/// feature: marking a root modified because a child changed is a per-aggregate rule, and belongs to
/// the feature's flusher, which runs first for exactly that reason.</summary>
public sealed class AuditingSaveChangesInterceptor(
    IAuditActor auditActor,
    IDateTimeProvider dateTimeProvider) : SaveChangesInterceptor
{
    /// <summary>Stamps who wrote and when, on every added or modified auditable row.</summary>
    /// <param name="eventData">EF's description of the save.</param>
    /// <param name="result">What an earlier interceptor decided; passed through.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <summary>The asynchronous half of <see cref="SavingChanges"/>, stamping the same rows.</summary>
    /// <param name="eventData">EF's description of the save.</param>
    /// <param name="result">What an earlier interceptor decided; passed through.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
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

        var now = dateTimeProvider.UtcNow;
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
