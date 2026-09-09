using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Entities;

/// <summary>
/// The stored values <see cref="StoredFile.Rehydrate"/> rebuilds a file from.
/// <para>
/// A parameter object rather than eleven parameters. Seven of them are value objects the persistence
/// layer builds in a row through their own factories, and two more are instants of the same type,
/// so an argument in the wrong position compiles wherever the types happen to line up. Naming every
/// value at the call site removes that class of mistake instead of relying on the order being read
/// carefully.
/// </para>
/// <para>
/// A class and not a record: it carries a sequence of tags, so value equality would compare that
/// sequence by reference and mean nothing. Nothing compares two of these.
/// </para>
/// </summary>
public sealed class StoredFileSnapshot
{
    /// <summary>The file's identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Who deposited the file.</summary>
    public required UserId OwnerId { get; init; }

    /// <summary>Where the content lives in the object store.</summary>
    public required ObjectKey ObjectKey { get; init; }

    /// <summary>The name a download is offered under.</summary>
    public required StoredFileName Name { get; init; }

    /// <summary>What the client said the content is.</summary>
    public required DeclaredMediaType DeclaredMediaType { get; init; }

    /// <summary>The content's length.</summary>
    public required FileSize Size { get; init; }

    /// <summary>The digest the client promised.</summary>
    public required Sha256Checksum Checksum { get; init; }

    /// <summary>How far the deposit got.</summary>
    public required StoredFileState State { get; init; }

    /// <summary>When the registration was written.</summary>
    public required DateTimeOffset RegisteredAt { get; init; }

    /// <summary>When the file became servable, or <c>null</c>.</summary>
    public required DateTimeOffset? AvailableAt { get; init; }

    /// <summary>The tags the row holds. Re-checked against the cap and for duplicates on the way in.</summary>
    public required IEnumerable<string> Tags { get; init; }
}
