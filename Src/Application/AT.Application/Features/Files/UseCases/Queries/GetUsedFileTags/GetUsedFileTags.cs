using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;
using Mediator;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;

public sealed record GetUsedFileTagsQuery : IQuery<Result<IReadOnlyList<string>>>;

public sealed class GetUsedFileTagsQueryHandler(
    IStoredFileTagQueries queries,
    ICacheStore cache,
    ICurrentUser currentUser) : IQueryHandler<GetUsedFileTagsQuery, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(GetUsedFileTagsQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<IReadOnlyList<string>>();
        }

        return Result.Success(await cache.GetOrCreateAsync(
            UsedTagsCache.KeyFor(UsedTagsCache.FileScope, userId.Value),
            token => queries.GetUsedTagsForOwnerAsync(userId.Value, token),
            UsedTagsCache.Lifetime,
            cancellationToken));
    }
}
