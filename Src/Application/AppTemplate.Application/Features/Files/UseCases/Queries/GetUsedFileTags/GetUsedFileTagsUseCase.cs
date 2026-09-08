using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;

/// <summary>
/// Read through <see cref="ICacheStore"/>: a picker asks for this on every screen that offers a
/// tag, and the answer changes only when this owner tags something.
/// <see cref="UsedTagsCache"/> holds the key and the lifetime, and the command that replaces a
/// file's tags drops the entry.
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

        // Result.Success rather than the implicit conversion: C# forbids a user-defined
        // conversion from an interface type, and the value here is an IReadOnlyList.
        return Result.Success(await cache.GetOrCreateAsync(
            UsedTagsCache.KeyFor(UsedTagsCache.FileScope, userId.Value),
            token => queries.GetUsedTagsForOwnerAsync(userId.Value, token),
            UsedTagsCache.Lifetime,
            cancellationToken));
    }
}
