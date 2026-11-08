namespace AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

public sealed record IssuedDownloadGrant(string Url, DateTimeOffset ExpiresAt);
