namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>Where a stored file is in its one-way life; the row and the blob are two writes with no
/// shared transaction, so each state names a moment when they are known to disagree. No member may
/// mean "deleted": deleting removes the row, and a "gone" state would put a predicate in every query.
/// A state added here is refused by default, because the one place that hands out bytes asks for
/// <see cref="Available"/> by name. The numbers are the persisted column, not the life order.</summary>
public enum StoredFileState
{
    /// <summary>Registered, key reserved, nothing written yet; <c>StoredFile.RegisteredAt</c> lets a
    /// sweep remove one never completed.</summary>
    Pending = 0,

    /// <summary>The bytes are present and match the declared size and digest; not servable, because
    /// confirming says the bytes arrived intact, not what they are. The abandonment sweep never
    /// touches it (<c>IsAbandoned</c> reads <see cref="Pending"/> alone).</summary>
    Deposited = 2,

    /// <summary>Confirmed and examined. The only state in which the file may be served.</summary>
    Available = 1,

    /// <summary>Examined and refused; terminal, since the verdict is about bytes that cannot change.
    /// The row survives so the owner sees the refusal; the reason is not recorded on it, because the
    /// operator's log can name a malware signature without reaching the uploader.</summary>
    Quarantined = 3,
}
