using AppTemplate.Domain.Core.Common.Primitives;
namespace AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;

/// <summary>
/// The vocabulary an owner has built up with their file tags, apart from the file queries because it is the one
/// answer worth caching.
/// </summary>
public interface IStoredFileTagQueries
{
    /// <summary>
    /// The distinct tags on this owner's files, each once and ordered, counted in the database rather than by loading
    /// files.
    /// </summary>
    Task<IReadOnlyList<string>> GetUsedTagsForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken = default);
}
