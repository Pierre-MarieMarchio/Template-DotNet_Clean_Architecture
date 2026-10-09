namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

/// <summary>What the store reports about the object under a key.</summary>
/// <param name="SizeInBytes">The object's length as the store measured it.</param>
/// <param name="Checksum">
/// The SHA-256 of the whole object, lower-case hexadecimal. An adapter produces it from the bytes or
/// from a digest the store recorded at write time — never from an entity tag, which is not a SHA-256.
/// </param>
public sealed record StoredObjectDescription(long SizeInBytes, string Checksum);
