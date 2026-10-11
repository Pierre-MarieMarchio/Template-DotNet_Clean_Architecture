namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

/// <summary>How much of one owner's allowance is already spoken for: bytes on the store and bytes
/// promised. Three of the four states mean the bytes are on the store (deposited, available,
/// quarantined), and a quarantined object is never reclaimed while its row exists, so counting only
/// available files would make a refused file the cheapest way to store bytes for ever.</summary>
/// <param name="StoredCount">Files whose bytes are on the store, in any of those three states.</param>
/// <param name="StoredBytes">What they weigh; equal to the declared size, since the length is bound into the
/// signature.</param>
/// <param name="PendingCount">Registrations waiting for a deposit: a reserved key and a signed URL, no bytes.</param>
/// <param name="PendingDeclaredBytes">What those registrations claim they will weigh. Counted, or a quota could be
/// walked past by registering everything first and depositing afterwards.</param>
public sealed record OwnerStorageUsage(
    int StoredCount,
    long StoredBytes,
    int PendingCount,
    long PendingDeclaredBytes)
{
    /// <summary>Every row this owner has, in any state.</summary>
    public int TotalCount => StoredCount + PendingCount;

    /// <summary>Stored bytes plus the bytes already promised. What the quota is measured against.</summary>
    public long CommittedBytes => StoredBytes + PendingDeclaredBytes;
}
