using AppTemplate.Domain.Core.Common.Primitives;
namespace AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;

public interface IStoredFileTagQueries
{
    Task<IReadOnlyList<string>> GetUsedTagsForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken = default);
}
