using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Domain.Features.Files.Events;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.Consumers.StoredFileDeleted;

/// <summary>
/// Deletes a file's bytes as soon as its row is gone, instead of waiting for the sweep.
/// <para>
/// A fast path, not the guarantee: <c>ReclaimOrphanedContentUseCase</c> reclaims whatever this never
/// reached, so it only shortens the interval (see "Correctness does not depend on event delivery" in
/// <c>docs/DECISIONS.md</c>). Running twice deletes an object already gone, which the store treats as
/// success. It commits nothing: it touches only the object store.
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
            // Shutdown, not a failure to reclaim: the sweep will find these bytes.
            throw;
        }
        catch (Exception exception)
        {
            // Swallowed: the deletion already committed and the sweep reclaims the bytes; propagating
            // would report a store outage as a failed deletion.
            logger.LogWarning(
                exception,
                "Reclaiming the content of stored file {StoredFileId} failed; the orphan sweep will " +
                "remove it instead.",
                domainEvent.StoredFileId);
        }
    }
}
