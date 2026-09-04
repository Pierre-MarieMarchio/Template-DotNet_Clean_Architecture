namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

/// <summary>
/// A right to deposit one object, as the client sees it — the same shape as
/// <c>IssuedRefreshToken</c>: a credential plus the instant it stops working.
/// </summary>
/// <param name="Url">Signed, and therefore a bearer right: it is handed to exactly one client and
/// it authorises whoever holds it.</param>
/// <param name="Method">The HTTP method the signature covers. A grant signed for one method does
/// not authorise another, so the client cannot be left to guess it.</param>
/// <param name="RequiredHeaders">
/// The headers the signature covers, which the deposit must send back verbatim — content type,
/// length, and whatever digest header the store checks. Omitting one is refused by the store, so
/// they travel with the URL rather than being described in prose the client has to follow.
/// </param>
/// <param name="ExpiresAt">Short by design. A grant that outlives the upload it was minted for is
/// a write right sitting in somebody's logs.</param>
public sealed record IssuedUploadGrant(
    string Url,
    string Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);
