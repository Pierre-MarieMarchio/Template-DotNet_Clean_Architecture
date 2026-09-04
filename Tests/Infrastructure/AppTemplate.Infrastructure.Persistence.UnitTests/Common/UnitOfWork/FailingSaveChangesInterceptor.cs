using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.UnitOfWork;

/// <summary>
/// Makes a save fail the way the database would, without a database: the exception is raised before EF
/// asks the provider for a connection.
/// </summary>
internal sealed class FailingSaveChangesInterceptor(Exception failure) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default) => throw failure;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result) => throw failure;
}
