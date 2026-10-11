using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Repositories;

namespace AppTemplate.Application.Features.Files.Consumers.AccountDeleted;

/// <summary>
/// The stored files' declared fate for the data of a deleted account: <b>deleted</b>, rows and bytes.
/// Each removal goes through <c>StoredFile.Delete</c> like any other, so the
/// <c>StoredFileDeletedDomainEvent</c> it raises reclaims the bytes promptly, and the orphan sweep
/// reclaims them anyway if that consumer is never reached — the row, not the event, is what makes
/// the bytes garbage.
/// <para>
/// This runs after the transaction that removed the account has already committed, so it owns
/// committing its own change through <see cref="IUnitOfWork"/>. <b>A fast path, not the guarantee</b>:
/// the same <see cref="IDeletedAccountDataPolicy"/> face the sweep calls covers the event never
/// reaching this consumer, and the precondition is re-derived through
/// <see cref="IUserAccountDirectory"/> rather than trusted.
/// </para>
/// </summary>
internal sealed class DeleteStoredFilesOnAccountDeletedConsumer(
    IStoredFileRepository files,
    IStoredFileQueries queries,
    IUserAccountDirectory accounts,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : IDomainEventConsumer<UserAccountDeleted>, IDeletedAccountDataPolicy
{
    public Task ConsumeAsync(UserAccountDeleted domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return ApplyAsync(domainEvent.UserId, cancellationToken);
    }

    public async Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // The precondition, re-derived: an account that exists again — or never left — keeps its
        // files, whatever this call was told.
        if (await accounts.ExistsAsync(owner, cancellationToken))
        {
            return false;
        }

        var owned = await files.GetForOwnerAsync(owner, cancellationToken);

        // Also what a redelivery finds.
        if (owned.Count == 0)
        {
            return false;
        }

        var deletedAt = dateTimeProvider.UtcNow;

        foreach (var file in owned)
        {
            file.Delete(deletedAt);
            files.Remove(file);
        }

        // One commit for the owner: the rows go together, and the events the deletions raised are
        // published by the same save that made them true.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
        CancellationToken cancellationToken = default) =>
        queries.ListDataOwnerIdsAsync(cancellationToken);
}
