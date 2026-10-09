namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>One object standing in the in-memory store, exactly as a deposit left it.</summary>
/// <param name="ObjectKey">What the object is filed under.</param>
/// <param name="MediaType">What the deposit said the bytes were.</param>
/// <param name="SizeInBytes">How many bytes were deposited, measured rather than declared.</param>
/// <param name="Checksum">The SHA-256 of the bytes, lower-case hexadecimal, computed here as a real
/// store computes it.</param>
/// <param name="DepositedAt">The instant of the deposit, from the injected clock.</param>
/// <param name="Content">The bytes themselves, so inspection and streamed downloads run against
/// real content.</param>
public sealed record DepositedObject(
    string ObjectKey,
    string MediaType,
    long SizeInBytes,
    string Checksum,
    DateTimeOffset DepositedAt,
    ReadOnlyMemory<byte> Content);
