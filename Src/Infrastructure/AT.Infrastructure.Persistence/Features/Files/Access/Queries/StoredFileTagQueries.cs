using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Access.Queries;

internal sealed class StoredFileTagQueries(AppDbContext context) : IStoredFileTagQueries
{
    public async Task<IReadOnlyList<string>> GetUsedTagsForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken = default) =>
        await context.StoredFiles
            .AsNoTracking()
            .Where(file => file.OwnerId == ownerId.Value)
            .SelectMany(file => file.Tags)
            .Select(tag => tag.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);
}
