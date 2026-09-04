namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

/// <summary>
/// A right to read one object. What the API hands back instead of the bytes: the controller answers
/// <c>302</c> with <see cref="Url"/>, and the content travels between the client and the store
/// without entering this process.
/// </summary>
/// <param name="Url">
/// Signed, and therefore a bearer right — anyone holding it can read the file, with no further
/// authentication anywhere. That is what makes <see cref="ExpiresAt"/> load-bearing rather than
/// cosmetic, and why a caller must never persist, log or cache this value.
/// </param>
/// <param name="ExpiresAt">
/// Minutes, not hours. The URL will end up in a browser's history, a referrer header and a proxy
/// log; a short window is the only thing that limits what those copies are still worth.
/// </param>
public sealed record IssuedDownloadGrant(string Url, DateTimeOffset ExpiresAt);
