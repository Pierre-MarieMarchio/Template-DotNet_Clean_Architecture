namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>One object standing in the store, exactly as a deposit left it.</summary>
/// <param name="ObjectKey">What the object is filed under. The only thing that addresses it.</param>
/// <param name="MediaType">What the deposit said the bytes were.</param>
/// <param name="SizeInBytes">How many bytes were deposited, measured rather than declared.</param>
/// <param name="Checksum">
/// A SHA-256 of the deposited bytes, as lower-case hexadecimal — computed here for the same reason a
/// real adapter has the store compute it: confirmation is worth something only because this value
/// did not come from whoever is asking for it to be confirmed.
/// </param>
/// <param name="DepositedAt">The instant the deposit happened, taken from the injected clock.</param>
/// <param name="Head">
/// The leading bytes of what was deposited, capped at <c>ContentInspectionOutcome.MaxHeadBytes</c>
/// exactly as a real adapter caps its own read.
/// <para>
/// <b>The only content this double keeps, and it keeps it so that inspection is testable against
/// real bytes.</b> Everything else here is a measurement, because the whole feature is arranged so
/// that content does not pass through this process — but deciding what a file is means reading its
/// first kibibyte, and a double that made a test <em>declare</em> the answer would let the signature
/// table be wrong and every test stay green. A test deposits an actual SVG and an actual refusal
/// comes back, through the real policy.
/// </para>
/// </param>
public sealed record DepositedObject(
    string ObjectKey,
    string MediaType,
    long SizeInBytes,
    string Checksum,
    DateTimeOffset DepositedAt,
    ReadOnlyMemory<byte> Head);
