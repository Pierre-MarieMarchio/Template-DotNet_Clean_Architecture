using AppTemplate.Application.Core.Common.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving;

internal sealed class EfUnitOfWork<TContext>(TContext context) : IContextUnitOfWork<TContext>
    where TContext : DbContext
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                "A concurrent modification of the same aggregate was detected; this write was rejected.",
                exception);
        }
    }
}
