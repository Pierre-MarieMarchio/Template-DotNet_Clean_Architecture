namespace AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

/// <summary>A right to deposit one object, as the client sees it.</summary>
/// <param name="Url">Signed, so a bearer right handed to exactly one client.</param>
/// <param name="Method">The HTTP method the signature covers.</param>
/// <param name="RequiredHeaders">The signed headers the deposit must send back verbatim — content
/// type, length and digest.</param>
/// <param name="ExpiresAt">Short by design: a grant outliving its upload is a write right in
/// somebody's logs.</param>
public sealed record IssuedUploadGrant(
    string Url,
    string Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);
