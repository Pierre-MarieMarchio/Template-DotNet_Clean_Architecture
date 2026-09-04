using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;

/// <summary>
/// Translates between the <see cref="StoredFile"/> aggregate and the row that stores it.
/// <para>
/// <b>For this aggregate the round-trip fidelity test is not hygiene, it is the barrier between a
/// mapping bug and destroyed user data.</b> A mapper that forgets a property usually loses a value,
/// which surfaces later as a field that "reset itself". Here one of the properties is
/// <c>ObjectKey</c>, and it is the only record anywhere of where a file's bytes were put: the store
/// keeps no index of which objects are owed, so the bytes are reclaimed by deleting every object no
/// row names. A mapper that writes a key differing from the one the bytes were deposited under —
/// dropped, truncated by a column too short, trimmed, re-cased, or minted afresh — therefore does not
/// lose a field. It makes a live file's content unreferenced, and the next orphan sweep deletes it.
/// The row survives, pointing at nothing, and the user's file is gone with no error anywhere.
/// </para>
/// <para>
/// So <c>StoredFileMapperFidelityTests</c> and <c>StoredFileMapperWriteFidelityTests</c> — which
/// enumerate the aggregate's state by reflection and fail on any property that does not survive
/// aggregate → record → aggregate — are what stands between a one-line edit here and that outcome,
/// and <c>StoredFileMapperObjectKeyTests</c> states the same guarantee for the key alone, in the terms
/// above. <b>Weakening any of the three removes that barrier and nothing replaces it.</b>
/// </para>
/// </summary>
internal interface IStoredFileMapper
{
    StoredFile ToAggregate(StoredFileRecord record);

    /// <summary>Builds the row for an aggregate that has never been stored.</summary>
    StoredFileRecord ToNewRecord(StoredFile aggregate);

    /// <summary>
    /// Writes the aggregate's current state onto an already-tracked row and lets EF's own diff decide
    /// what to write. Returns nothing: a stored file has no child collection whose reconciliation could
    /// leave the root looking unchanged, so there is nothing for a caller to act on beyond what EF's
    /// change tracker already sees.
    /// </summary>
    void WriteTo(StoredFile aggregate, StoredFileRecord record);
}
