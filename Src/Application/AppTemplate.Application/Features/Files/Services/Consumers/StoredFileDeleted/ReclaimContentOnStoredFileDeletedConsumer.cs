using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Domain.Features.Files.Model.Events;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.Services.Consumers.StoredFileDeleted;

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
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Reclaiming the content of stored file {StoredFileId} failed; the orphan sweep will " +
                "remove it instead.",
                domainEvent.StoredFileId);
        }
    }
}
