namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>One object in the in-memory store, as a deposit left it; size and checksum are measured.</summary>
public sealed record DepositedObject(
    string ObjectKey,
    string MediaType,
    long SizeInBytes,
    string Checksum,
    DateTimeOffset DepositedAt,
    ReadOnlyMemory<byte> Content);
