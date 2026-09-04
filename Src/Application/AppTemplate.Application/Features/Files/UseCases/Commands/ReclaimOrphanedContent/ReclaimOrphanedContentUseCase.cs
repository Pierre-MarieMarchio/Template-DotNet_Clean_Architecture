using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

/// <summary>
/// Deletes stored objects that no row names. This is where the storage a deleted file used to
/// occupy actually goes away, and it is the only thing in the feature that guarantees it.
/// <para>
/// <b>It re-derives its own precondition rather than consuming a message.</b>
/// <c>StoredFileDeletedDomainEvent</c> has a consumer, but that consumer is a fast path: events
/// here are dispatched in-process, after commit, at most once, so one may simply not run. This pass
/// asks a question whose answer does not depend on any delivery having happened — "is there a row
/// that names this key?" — and it covers a case no event could: bytes deposited against a signed
/// URL whose registration was swept away before it was ever confirmed, for which nothing was ever
/// raised at all.
/// </para>
/// <para>
/// <b>The order of the two reads is the safety argument, and reversing it deletes live files.</b>
/// Each page of keys is listed <em>first</em> and the rows are asked about it <em>second</em>. A
/// file registered while the pass is running therefore either appears in a later page — by which
/// time its row certainly exists, because a row is committed before its upload grant is ever minted
/// — or does not appear at all. Reading the live keys first and listing afterwards would invert
/// that: a file registered in between would be listed as an object with no row on record, and the
/// pass would delete the bytes of a file its owner had just uploaded.
/// </para>
/// <para>
/// <b>Deleting the same object twice is deleting it once</b> (see
/// <see cref="IFileContentStore.DeleteAsync"/>), so two hosts sweeping at the same time cost a
/// duplicate request and nothing else. That is why this takes no <see cref="ILeaderLease"/>, on the
/// same reasoning that port's own documentation gives for the two purges.
/// </para>
/// <para>
/// <b>Runs from <c>AppTemplate.Worker</c>, so it must not read <see cref="ICurrentUser"/>:</b> that
/// host's implementation throws rather than invent an anonymous caller, and the objects swept here
/// belong to every owner and to none of them.
/// </para>
/// </summary>
public sealed class ReclaimOrphanedContentUseCase(
    IFileContentInventory inventory,
    IFileContentStore content,
    IStoredFileQueries queries,
    ILogger<ReclaimOrphanedContentUseCase> logger) : IReclaimOrphanedContentUseCase
{
    /// <summary>
    /// How many keys one listing call asks for. Also the size of the batch each live-key lookup is
    /// asked about, so this bounds both halves of the difference and the pass holds one page at a
    /// time however large the bucket is.
    /// </summary>
    private const int _pageSize = 500;

    /// <summary>
    /// The safety valve on one pass. At <see cref="_pageSize"/> keys a page this is a quarter of a
    /// million objects, past which the pass stops and the next one resumes from the start — the
    /// listing is ordered, and every orphan the previous pass reached is already gone, so restarting
    /// makes progress rather than repeating work.
    /// </summary>
    private const int _maxPagesPerPass = 500;

    /// <summary>
    /// The whole key namespace. Keys are minted as
    /// <c>&lt;partition&gt;/&lt;time segment&gt;/&lt;name&gt;</c> and listings are ordered, so
    /// walking from here arrives one time slice at a time on its own: each page falls inside a
    /// single segment, and the lookup that follows reads only the rows that page names. Narrowing
    /// this to one segment is a supported refinement — the correctness argument above holds for any
    /// prefix — but it would mean choosing which slice a pass visits, and a slice nobody chose is a
    /// slice never swept.
    /// </summary>
    private static readonly string _prefix = $"{ObjectKey.UnpartitionedPrefix}/";

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        int reclaimed = 0;
        int pages = 0;
        string? continuationToken = null;

        do
        {
            var page = await inventory.ListKeysAsync(_prefix, continuationToken, _pageSize, cancellationToken);

            pages++;
            continuationToken = page.NextCursor;

            if (page.Items.Count == 0)
            {
                continue;
            }

            var live = (await queries.GetLiveObjectKeysAsync(page.Items, cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            foreach (string objectKey in page.Items)
            {
                if (live.Contains(objectKey))
                {
                    continue;
                }

                await content.DeleteAsync(objectKey, cancellationToken);
                reclaimed++;
            }
        }
        while (continuationToken is not null && pages < _maxPagesPerPass);

        if (continuationToken is not null && logger.IsEnabled(LogLevel.Information))
        {
            // Worth saying out loud: a pass that stopped short did not finish the store, and a
            // steady stream of these means the bucket has outgrown one pass rather than that
            // nothing needed reclaiming.
            logger.LogInformation(
                "Orphan sweep stopped after {Pages} pages with more of the store left to walk; " +
                "{Reclaimed} objects were reclaimed.",
                pages,
                reclaimed);
        }

        return reclaimed;
    }
}
