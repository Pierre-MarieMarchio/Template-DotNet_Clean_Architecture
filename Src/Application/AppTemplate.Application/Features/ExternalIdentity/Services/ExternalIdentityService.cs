using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Model.Errors;
using AppTemplate.Application.Features.ExternalIdentity.Model.Policies;
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityStore;
using AppTemplate.Application.Features.ExternalIdentity.Ports.RoleAssignmentQueries;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Services;

internal sealed class ExternalIdentityService(
    IExternalIdentityStore store,
    IExternalIdentityQueries queries,
    IRoleAssignmentQueries roleAssignments,
    IDomainEventPublisher domainEvents,
    IDateTimeProvider dateTimeProvider) : IExternalIdentityService
{
    internal static readonly TimeSpan LastSeenGranularity = TimeSpan.FromHours(1);

    public async Task<UserId> ResolveAsync(ExternalCallerDto caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller.Subject);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caller.Issuer.Length, ExternalIdentityPolicy.IssuerMaxLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caller.Subject.Length, ExternalIdentityPolicy.SubjectMaxLength);

        var now = dateTimeProvider.UtcNow;
        string? email = KeptEmail(caller.Email);
        string? displayName = KeptDisplayName(caller.DisplayName);

        var existing = await store.FindAsync(caller.Issuer, caller.Subject, cancellationToken);

        if (existing is null)
        {
            var created = new ExternalIdentityLink(
                caller.Issuer,
                caller.Subject,
                UserId.Create(Guid.CreateVersion7()),
                email,
                displayName,
                now);

            if (await store.TryAddAsync(created, cancellationToken))
            {
                return created.UserId;
            }

            existing = await store.FindAsync(caller.Issuer, caller.Subject, cancellationToken)
                ?? throw new InvalidOperationException(
                    "The mapping insert was refused as a duplicate, yet no mapping for the pair can be read.");
        }

        bool profileChanged = !string.Equals(existing.Email, email, StringComparison.Ordinal)
            || !string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal);

        if (profileChanged || now - existing.LastSeenAt >= LastSeenGranularity)
        {
            await store.RefreshAsync(
                existing with { Email = email, DisplayName = displayName, LastSeenAt = now },
                cancellationToken);
        }

        return existing.UserId;
    }

    public Task<IReadOnlyList<string>> ListAssignedRolesAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return roleAssignments.ListRolesAsync(userId, cancellationToken);
    }

    public Task<ExternalIdentityProfile?> FindProfileAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return queries.FindProfileAsync(userId, cancellationToken);
    }

    public async Task<Result> DeleteAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        int removed = await store.RemoveAsync(userId, cancellationToken);

        if (removed == 0)
        {
            return Result.Failure(ExternalIdentityErrors.MappingNotFound);
        }

        await domainEvents.PublishAsync(
            new UserAccountDeleted(userId, dateTimeProvider.UtcNow),
            cancellationToken);

        return Result.Success();
    }

    private static string? KeptEmail(string? email)
    {
        string? trimmed = email?.Trim();

        return string.IsNullOrEmpty(trimmed) || trimmed.Length > ExternalIdentityPolicy.EmailMaxLength
            ? null
            : trimmed;
    }

    private static string? KeptDisplayName(string? displayName)
    {
        string? trimmed = displayName?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > ExternalIdentityPolicy.DisplayNameMaxLength
            ? trimmed[..ExternalIdentityPolicy.DisplayNameMaxLength]
            : trimmed;
    }
}
