namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

/// <summary>
/// What the store reports about the object under a key. The only facts in this feature that the
/// client did not author, which is why confirmation compares against these and not against a second
/// copy of the client's own declaration.
/// </summary>
/// <param name="SizeInBytes">The object's length as the store measured it.</param>
/// <param name="Checksum">
/// A SHA-256 digest of the whole object, as lower-case hexadecimal. <b>Producing that is the
/// adapter's job, not the caller's.</b> An object store's own entity tag is not a SHA-256 — it may
/// be an MD5, a digest of digests for a multipart upload, or an opaque token — and an adapter that
/// passed one through would make every confirmation fail with a mismatch that names nothing. Where
/// the store can be asked to compute and record a SHA-256 at deposit time, that is the value; where
/// it cannot, the adapter owes the computation.
/// </param>
public sealed record StoredObjectDescription(long SizeInBytes, string Checksum);
