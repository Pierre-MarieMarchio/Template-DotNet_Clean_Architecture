namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

public sealed record OwnerStorageUsage(
    int StoredCount,
    long StoredBytes,
    int PendingCount,
    long PendingDeclaredBytes)
{
    public int TotalCount => StoredCount + PendingCount;

    public long CommittedBytes => StoredBytes + PendingDeclaredBytes;
}
