using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;

/// <summary>
/// Translates between the <see cref="StoredFile"/> aggregate and its row.
/// <para>
/// A mapper bug here can destroy user data: <c>ObjectKey</c> is the only record of where a file's
/// bytes were put, and the orphan sweep deletes every object no row names. A key that is dropped,
/// truncated, trimmed, re-cased or minted afresh therefore unlinks a live file's content with no
/// error anywhere. <c>StoredFileMapperFidelityTests</c>, <c>StoredFileMapperWriteFidelityTests</c> and
/// <c>StoredFileMapperObjectKeyTests</c> are the barrier; weakening any of them removes it.
/// </para>
/// </summary>
internal interface IStoredFileMapper
{
    StoredFile ToAggregate(StoredFileRecord record);

    /// <summary>Builds the row for an aggregate that has never been stored.</summary>
    StoredFileRecord ToNewRecord(StoredFile aggregate);

    /// <summary>Writes the aggregate's state onto an already-tracked row and lets EF's diff decide what to write.</summary>
    void WriteTo(StoredFile aggregate, StoredFileRecord record);
}
