namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

public sealed record StoredObjectDescription(long SizeInBytes, string Checksum);
