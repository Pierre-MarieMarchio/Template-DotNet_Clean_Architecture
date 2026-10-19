using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.ExternalIdentity.Model.Dtos;
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Services;

/// <summary>Who a caller authenticated by an external provider is to this application, one method per
/// operation. The provider's subject is opaque: every caller is mapped, by (issuer, subject) and
/// never by email, to an internal <see cref="UserId"/> that the business rows carry.</summary>
public interface IExternalIdentityService
{
    /// <summary>The owner id mapped to the caller, creating the mapping on the caller's first
    /// request and refreshing its email and display name when the provider's have changed.</summary>
    /// <remarks>Each write is its own transaction, independent of the request's unit of work. Two
    /// first requests of the same caller race on the unique index over (issuer, subject): the loser's
    /// insert is refused and it reads the winner's row, so both answer the same owner — as does a
    /// retry of an insert that had in fact committed. A transient failure is retried by the
    /// execution strategy; anything else propagates: there is no fallback owner.</remarks>
    Task<UserId> ResolveAsync(ExternalCallerDto caller, CancellationToken cancellationToken = default);

    /// <returns>The roles assigned to the owner in this application's store; empty when none is.</returns>
    Task<IReadOnlyList<string>> ListAssignedRolesAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <returns>What the provider last said about the owner, or <c>null</c> when no mapping names it.</returns>
    Task<ExternalIdentityProfile?> FindProfileAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the account's local presence: every mapping that names the owner, and the
    /// role assignments held for it. The provider's own account is the provider's to delete; the only
    /// trigger the template wires is an administrative call — a provider-side deletion signal has no
    /// endpoint here, deliberately (see <c>docs/ARCHITECTURE.md</c>).</summary>
    /// <remarks>The store commits before this returns, in a transaction of its own; a second call
    /// answers <see cref="Model.Errors.ExternalIdentityErrors.MappingNotFound"/>. A caller that signs in
    /// again is provisioned afresh, under a new owner id with none of the old one's data. A transient
    /// failure is retried by replaying the whole transaction; anything else propagates and nothing is
    /// removed.</remarks>
    /// <param name="userId">The internal owner to delete.</param>
    /// <returns>Success, or the not-found error when no mapping names the owner.</returns>
    Task<Result> DeleteAsync(UserId userId, CancellationToken cancellationToken = default);
}
