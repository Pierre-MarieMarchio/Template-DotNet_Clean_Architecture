namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

/// <summary>
/// How much of one owner's allowance is already spoken for. Read before a registration is accepted,
/// and split in two because the two halves are not the same risk.
/// </summary>
/// <remarks>
/// <b>The split is "bytes on the store" against "bytes promised", not one state against another.</b>
/// <c>StoredFileState</c> has four members and three of them mean the bytes are on the store —
/// <c>Deposited</c> waiting for a verdict, <c>Available</c> serving, and <c>Quarantined</c> refused
/// and kept so its owner can be told. Counting only <c>Available</c> would leave the other two
/// weighing on the bucket and on nobody's quota, and <c>Quarantined</c> is terminal: nothing moves a
/// file out of it and no sweep reclaims its object, because a row still names it. Failing a file's
/// content would then be the cheapest way to store bytes for ever.
/// </remarks>
/// <param name="StoredCount">Files whose bytes are on the store, in any of the three states that
/// mean they are.</param>
/// <param name="StoredBytes">What those files weigh. Equal to what was declared, because a deposit
/// of any other length is refused by the store: the length is bound into the signature.</param>
/// <param name="PendingCount">Registrations waiting for a deposit. Each one is a reserved key and a
/// signed upload URL, and no bytes at all.</param>
/// <param name="PendingDeclaredBytes">
/// What those registrations claim they will weigh. Not storage yet, and it may never become any —
/// but every one of them is a deposit the client may complete at any moment before the abandonment
/// sweep removes it, so a quota that ignored this number could be walked straight past by
/// registering everything first and depositing afterwards.
/// </param>
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
