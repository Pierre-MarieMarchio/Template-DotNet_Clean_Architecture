namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// Where a stored file is in its one-way life. The row and the blob are two writes with no shared transaction, so
/// each state names a moment when they are known to disagree.
/// <para>
/// No member may mean "deleted": deleting removes the row (see <c>docs/DECISIONS.md</c>), and a "gone" state would
/// put a predicate in every query. <see cref="Quarantined"/> costs none, because the one place that hands out bytes
/// asks for <see cref="Available"/> by name, so a state added here is refused by default.
/// </para>
/// <para>
/// The numbers are the persisted column and differ from the life order on purpose: <see cref="Pending"/> and
/// <see cref="Available"/> keep the numbers existing rows were written with. Reordering members is free; renumbering
/// them reinterprets every row.
/// </para>
/// </summary>
public enum StoredFileState
{
    /// <summary>
    /// Registered, key reserved, nothing written yet. <c>StoredFile.RegisteredAt</c> lets a sweep remove one never
    /// completed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The bytes are present and match the declared size and digest; nothing has read them yet. Not servable:
    /// confirming a deposit says the bytes arrived intact, not what they are.
    /// <para>
    /// The abandonment sweep never touches it (<c>IsAbandoned</c> reads <see cref="Pending"/> alone), so an
    /// inspection backlog or an unreachable scanner never turns into deleted uploads.
    /// </para>
    /// </summary>
    Deposited = 2,

    /// <summary>Confirmed and examined. The only state in which the file may be served.</summary>
    Available = 1,

    /// <summary>
    /// Examined and refused. Terminal: the verdict is about bytes that cannot change, and a client wanting another
    /// answer uploads another file.
    /// <para>
    /// The row survives so the owner sees the refusal. Why it was refused is not recorded: the detail goes to the
    /// operator's log, where it can name a malware signature without reaching the uploader.
    /// </para>
    /// </summary>
    Quarantined = 3,
}
