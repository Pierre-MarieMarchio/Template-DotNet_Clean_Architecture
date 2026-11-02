namespace AppTemplate.Infrastructure.InMemory.Features.Files;

public sealed record DepositedObject(
    string ObjectKey,
    string MediaType,
    long SizeInBytes,
    string Checksum,
    DateTimeOffset DepositedAt,
    ReadOnlyMemory<byte> Content);
