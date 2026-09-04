using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Domain.Features.Files.Events;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.Consumers.StoredFileDeleted;

/// <summary>
/// Deletes a file's bytes as soon as its row is gone, instead of waiting for the sweep to notice
/// that nothing references them any more.
/// <para>
/// <b>This is a fast path, not the correctness guarantee.</b> Deleting this class would not leak a
/// single byte: <c>ReclaimOrphanedContentUseCase</c> lists the store, subtracts the keys live rows
/// name, and removes the difference — so an object this consumer never got to is reclaimed on the
/// next pass regardless. What the consumer buys is the interval: seconds instead of up to
/// <c>FileWorker:ReclaimOrphanedContentInterval</c>, which ships at twelve hours because a pass
/// walks the whole store.
/// </para>
/// <para>
/// That is the shape this repository requires of every consumer, because domain events are
/// dispatched in-process, after commit, at most once, with no outbox — see CONTRIBUTING.md. The
/// effect re-derives its own precondition: running twice deletes an object that is already gone,
/// which the store treats as success, and never running leaves the system consistent but holding
/// bytes for longer than it needed to.
/// </para>
/// <para>
/// It commits nothing, and needs no <see cref="IUnitOfWork"/>: it touches only the object store,
/// and the row whose deletion triggered it was committed before this ran.
/// </para>
/// </summary>
internal sealed class ReclaimContentOnStoredFileDeletedConsumer(
    IFileContentStore content,
    ILogger<ReclaimContentOnStoredFileDeletedConsumer> logger)
    : IDomainEventConsumer<StoredFileDeletedDomainEvent>
{
    public async Task ConsumeAsync(
        StoredFileDeletedDomainEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        try
        {
            await content.DeleteAsync(domainEvent.ObjectKey.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, not a failure to reclaim: the sweep will find these bytes. Rethrowing keeps
            // cancellation honest rather than reporting it as a store that refused.
            throw;
        }
        catch (Exception exception)
        {
            // Swallowed on purpose, and logged at warning rather than error: the object store being
            // briefly unreachable is not a failure of the delete the user asked for — that already
            // committed — and the sweep is what makes this recoverable without anyone retrying.
            // Letting it propagate would surface a store outage as a failed deletion, which is the
            // one thing that is not true.
            logger.LogWarning(
                exception,
                "Reclaiming the content of stored file {StoredFileId} failed; the orphan sweep will " +
                "remove it instead.",
                domainEvent.StoredFileId);
        }
    }
}
