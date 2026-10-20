namespace AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

public sealed record IssuedUploadGrant(
    string Url,
    string Method,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);
