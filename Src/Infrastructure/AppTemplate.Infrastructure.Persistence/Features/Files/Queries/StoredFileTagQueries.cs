using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Queries;

/// <summary>
/// The adapter for <see cref="IStoredFileTagQueries"/>. One column of a join, projected in SQL:
/// nothing is materialised or tracked.
/// </summary>
internal sealed class StoredFileTagQueries(AppDbContext context) : IStoredFileTagQueries
{
    public async Task<IReadOnlyList<string>> GetUsedTagsForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        await context.StoredFiles
            .AsNoTracking()
            .Where(file => file.OwnerId == ownerId)
            .SelectMany(file => file.Tags)
            .Select(tag => tag.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);
}
