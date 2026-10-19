using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityStore;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Access.Stores;

/// <summary>The adapter for <see cref="IExternalIdentityStore"/>: every call opens a context of its
/// own from the factory and, for a write, commits it before returning — not the request's context,
/// since the mapping is written while the request is being authenticated, and on the shared context a
/// refused insert would stay tracked as <c>Added</c> for the operation's own
/// <c>SaveChangesAsync</c> to retry, and an operation failure would roll back a mapping that is true
/// regardless. Each write is one statement under the retrying execution strategy; an insert that
/// committed but whose acknowledgement was lost is retried, meets the unique index, and is answered
/// from the row it had in fact written.</summary>
internal sealed class ExternalIdentityStore(IDbContextFactory<AppDbContext> contextFactory) : IExternalIdentityStore
{
    public async Task<ExternalIdentityLink?> FindAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var record = await context.ExternalIdentities
            .AsNoTracking()
            .SingleOrDefaultAsync(
                identity => identity.Issuer == issuer && identity.Subject == subject,
                cancellationToken);

        return record is null
            ? null
            : new ExternalIdentityLink(
                record.Issuer,
                record.Subject,
                UserId.Create(record.UserId),
                record.Email,
                record.DisplayName,
                record.LastSeenAt);
    }

    public async Task<bool> TryAddAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.ExternalIdentities.Add(new ExternalIdentityRecord
        {
            Id = Guid.CreateVersion7(),
            Issuer = link.Issuer,
            Subject = link.Subject,
            UserId = link.UserId.Value,
            Email = link.Email,
            DisplayName = link.DisplayName,
            CreatedAt = link.LastSeenAt,
            LastSeenAt = link.LastSeenAt,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException)
        {
            // Read back rather than matched on a provider's error code, so the decision is the same
            // on every provider: if the pair now exists, the index refused this insert because
            // somebody else's — or this call's own, retried — got there first.
            if (await FindAsync(link.Issuer, link.Subject, cancellationToken) is not null)
            {
                return false;
            }

            throw;
        }
    }

    public async Task RefreshAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // One UPDATE, never loading the row: the owner id is not in the SET list, so no refresh can
        // move a subject to another owner.
        await context.ExternalIdentities
            .Where(identity => identity.Issuer == link.Issuer && identity.Subject == link.Subject)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(identity => identity.Email, link.Email)
                    .SetProperty(identity => identity.DisplayName, link.DisplayName)
                    .SetProperty(identity => identity.LastSeenAt, link.LastSeenAt),
                cancellationToken);
    }

    public async Task<int> RemoveAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Two statements, one transaction, replayed as a whole by the context's retrying execution
        // strategy: an owner is deleted with its roles or not at all, and a retry never resumes
        // half of one.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            int removed = await context.ExternalIdentities
                .Where(identity => identity.UserId == userId.Value)
                .ExecuteDeleteAsync(cancellationToken);

            await context.RoleAssignments
                .Where(assignment => assignment.UserId == userId.Value)
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return removed;
        });
    }
}
