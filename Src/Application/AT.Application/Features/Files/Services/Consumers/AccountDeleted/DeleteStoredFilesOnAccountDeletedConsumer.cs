using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Repositories;

namespace AppTemplate.Application.Features.Files.Services.Consumers.AccountDeleted;

internal sealed class DeleteStoredFilesOnAccountDeletedConsumer(
    IStoredFileRepository files,
    IStoredFileQueries queries,
    IUserAccountDirectory accounts,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : IDomainEventConsumer<UserAccountDeleted>, IDeletedAccountDataPolicy
{
    public Task ConsumeAsync(UserAccountDeleted domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return ApplyAsync(domainEvent.UserId, cancellationToken);
    }

    public async Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (await accounts.ExistsAsync(owner, cancellationToken))
        {
            return false;
        }

        var owned = await files.GetForOwnerAsync(owner, cancellationToken);

        if (owned.Count == 0)
        {
            return false;
        }

        var deletedAt = dateTimeProvider.UtcNow;

        foreach (var file in owned)
        {
            file.Delete(deletedAt);
            files.Remove(file);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
        CancellationToken cancellationToken = default) =>
        queries.ListDataOwnerIdsAsync(cancellationToken);
}
