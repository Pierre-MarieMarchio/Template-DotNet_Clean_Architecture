using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;

/// <summary>
/// Read through <see cref="ICacheStore"/>, since a picker asks on every screen and the answer
/// changes only when this owner tags something; <see cref="UsedTagsCache"/> holds the key and
/// lifetime, and replacing a file's tags drops the entry.
/// </summary>
public sealed class GetUsedFileTagsUseCase(
    IStoredFileTagQueries queries,
    ICacheStore cache,
    ICurrentUser currentUser) : IGetUsedFileTagsUseCase
{
    public async Task<Result<IReadOnlyList<string>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<IReadOnlyList<string>>();
        }

        // Result.Success: C# has no user-defined implicit conversion from an interface type.
        return Result.Success(await cache.GetOrCreateAsync(
            UsedTagsCache.KeyFor(UsedTagsCache.FileScope, userId.Value),
            token => queries.GetUsedTagsForOwnerAsync(userId.Value, token),
            UsedTagsCache.Lifetime,
            cancellationToken));
    }
}
