namespace AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

/// <summary>A right to read one object: the API answers <c>302</c> to <see cref="Url"/>.</summary>
/// <param name="Url">Signed, so a bearer right: never persisted, logged or cached.</param>
/// <param name="ExpiresAt">Minutes away, which limits the copies that land in histories and proxy logs.</param>
public sealed record IssuedDownloadGrant(string Url, DateTimeOffset ExpiresAt);
