using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Domain.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;

/// <summary>
/// Stamps audit columns on the persistence models that opt in via <see cref="IAuditable"/>.
/// <para>
/// This replaces a reflection helper that tested for a generic base class, cast to
/// <c>dynamic</c>, and assigned properties by name — so a renamed property broke at
/// runtime, not at compile time — and that resolved the current user by
/// <c>Guid.Parse</c>-ing a claim string, which threw on a malformed token and produced
/// <c>Guid.Empty</c> for anything unauthenticated. Here the contract is an interface, the
/// dispatch is pattern matching, and an anonymous caller is recorded as <c>null</c>,
/// because "we do not know who did this" is a fact worth keeping and <c>Guid.Empty</c> is
/// a lie that looks like a user id.
/// </para>
/// <para>
/// <b>It stamps the record, not the aggregate.</b> The tracked entities are persistence models, and
/// they are what carries the audit columns; the interceptor therefore remains the single writer of
/// those four values. A mapper that copied audit values out of a domain object and into a row would
/// be the second writer, and the two would disagree — which is the defect this template was rescued
/// from. The flush pipeline runs <em>before</em> this interceptor and the read-back runs after, so a
/// domain aggregate is handed the values this interceptor decided rather than the other way round.
/// </para>
/// <para>
/// It knows no feature. Marking an aggregate root modified because one of its children changed is a
/// per-aggregate rule — it depends on which navigation leads back to the root — so it belongs to the
/// feature that owns the aggregate, in its flusher, which runs first for exactly that reason.
/// </para>
/// </summary>
internal sealed class AuditingSaveChangesInterceptor(
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider) : SaveChangesInterceptor
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

        var now = dateTimeProvider.UtcNow;
        var userId = currentUser.UserId;

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
