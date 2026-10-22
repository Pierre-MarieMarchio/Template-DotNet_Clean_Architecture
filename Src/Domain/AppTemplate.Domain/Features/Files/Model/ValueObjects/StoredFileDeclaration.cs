namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

public sealed record StoredFileDeclaration
{
    private StoredFileDeclaration(
        StoredFileName name,
        DeclaredMediaType mediaType,
        FileSize size,
        Sha256Checksum checksum)
    {
        Name = name;
        MediaType = mediaType;
        Size = size;
        Checksum = checksum;
    }

    public StoredFileName Name { get; }

    public DeclaredMediaType MediaType { get; }

    public FileSize Size { get; }

    public Sha256Checksum Checksum { get; }

    public static StoredFileDeclaration Create(
        StoredFileName name,
        DeclaredMediaType mediaType,
        FileSize size,
        Sha256Checksum checksum)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(mediaType);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(checksum);

        return new StoredFileDeclaration(name, mediaType, size, checksum);
    }
}
