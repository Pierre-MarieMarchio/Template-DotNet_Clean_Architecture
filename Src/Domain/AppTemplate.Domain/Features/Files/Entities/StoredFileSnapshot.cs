using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Entities;

/// <summary>
/// The stored values <see cref="StoredFile.Rehydrate"/> rebuilds a file from. A parameter object so
/// every value is named at the call site: several share a type, and a misplaced argument would compile.
/// A class and not a record, since value equality over the tag sequence would mean nothing.
/// </summary>
public sealed class StoredFileSnapshot
{
    /// <summary>The file's identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Who deposited the file.</summary>
    public required UserId OwnerId { get; init; }

    /// <summary>The category the file was registered under.</summary>
    public required FileCategory Category { get; init; }

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

    /// <summary>How many inspections reached no verdict.</summary>
    public required int InspectionAttempts { get; init; }

    /// <summary>The earliest instant of the next inspection, or <c>null</c>.</summary>
    public required DateTimeOffset? NextInspectionAt { get; init; }

    /// <summary>The tags the row holds. Re-checked against the cap and for duplicates on the way in.</summary>
    public required IEnumerable<string> Tags { get; init; }
}
