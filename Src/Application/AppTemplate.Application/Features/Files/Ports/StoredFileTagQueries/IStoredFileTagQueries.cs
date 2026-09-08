namespace AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;

/// <summary>
/// What this owner has labelled their files with. One capability of its own rather than a fifth
/// method on the file queries: reading a file and reading the vocabulary the owner has built up are
/// different questions, and only one of them is worth caching.
/// </summary>
public interface IStoredFileTagQueries
{
    /// <summary>
    /// The distinct tags this owner has put on their files. Counted in the database rather than by
    /// loading their files: the answer is one column of a join, and materialising every file to
    /// collect it would cost more than the picker it fills.
    /// </summary>
    /// <returns>Every tag value in use, each once, ordered so two calls agree.</returns>
    Task<IReadOnlyList<string>> GetUsedTagsForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
