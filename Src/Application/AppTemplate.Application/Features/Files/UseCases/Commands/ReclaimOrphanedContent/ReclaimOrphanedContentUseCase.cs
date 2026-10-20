using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

public sealed class ReclaimOrphanedContentUseCase : IReclaimOrphanedContentUseCase
{
    internal const int DefaultPageSize = 500;

    internal const int DefaultMaxPagesPerPass = 500;

    private const string _leaseName = "apptemplate.files.reclaim-orphans";

    private static readonly string _prefix = $"{ObjectKey.UnpartitionedPrefix}/";

    private readonly IFileContentInventory _inventory;
    private readonly IFileContentStore _content;
    private readonly IStoredFileQueries _queries;
    private readonly IOrphanSweepCheckpoint _checkpoint;
    private readonly ILeaderLease _lease;
    private readonly ILogger<ReclaimOrphanedContentUseCase> _logger;
    private readonly int _pageSize;
    private readonly int _maxPagesPerPass;

    public ReclaimOrphanedContentUseCase(
        IFileContentInventory inventory,
        IFileContentStore content,
        IStoredFileQueries queries,
        IOrphanSweepCheckpoint checkpoint,
        ILeaderLease lease,
        ILogger<ReclaimOrphanedContentUseCase> logger)
        : this(inventory, content, queries, checkpoint, lease, logger, DefaultPageSize, DefaultMaxPagesPerPass)
    {
    }

    internal ReclaimOrphanedContentUseCase(
        IFileContentInventory inventory,
        IFileContentStore content,
        IStoredFileQueries queries,
        IOrphanSweepCheckpoint checkpoint,
        ILeaderLease lease,
        ILogger<ReclaimOrphanedContentUseCase> logger,
        int pageSize,
        int maxPagesPerPass)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPagesPerPass);

        _inventory = inventory;
        _content = content;
        _queries = queries;
        _checkpoint = checkpoint;
        _lease = lease;
        _logger = logger;
        _pageSize = pageSize;
        _maxPagesPerPass = maxPagesPerPass;
    }

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        int reclaimed = 0;

        bool ranHere = await _lease.TryRunExclusivelyAsync(
            _leaseName,
            async cancellationToken => reclaimed = await SweepAsync(cancellationToken),
            cancellationToken);

        if (!ranHere && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Orphan sweep skipped: another host holds the '{LeaseName}' lease.",
                _leaseName);
        }

        return reclaimed;
    }

    private async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        string? startAfter = await _checkpoint.GetAsync(cancellationToken);
        int reclaimed = 0;
        int pages = 0;

        do
        {
            var page = await _inventory.ListKeysAsync(_prefix, startAfter, _pageSize, cancellationToken);
            pages++;

            reclaimed += await ReclaimAsync(page.Items, cancellationToken);

            startAfter = page.NextCursor;
            await _checkpoint.SaveAsync(startAfter, cancellationToken);
        }
        while (startAfter is not null && pages < _maxPagesPerPass);

        if (startAfter is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Orphan sweep reached its budget of {Pages} pages and resumes after '{Checkpoint}' next " +
                "pass; {Reclaimed} objects were reclaimed.",
                pages,
                startAfter,
                reclaimed);
        }

        return reclaimed;
    }

    private async Task<int> ReclaimAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return 0;
        }

        var live = (await _queries.GetLiveObjectKeysAsync(keys, cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        int reclaimed = 0;

        foreach (string objectKey in keys)
        {
            if (live.Contains(objectKey))
            {
                continue;
            }

            await _content.DeleteAsync(objectKey, cancellationToken);
            reclaimed++;
        }

        return reclaimed;
    }
}
