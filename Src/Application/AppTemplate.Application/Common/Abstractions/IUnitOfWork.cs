namespace AppTemplate.Application.Common.Abstractions;

/// <summary>Repositories only stage changes; the use case decides when they commit.</summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits all staged changes as one transaction and dispatches the domain
    /// events raised by the aggregates involved.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
