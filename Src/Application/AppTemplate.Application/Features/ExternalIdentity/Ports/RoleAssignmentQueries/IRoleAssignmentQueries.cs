using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Ports.RoleAssignmentQueries;

/// <summary>
/// The roles assigned to an owner in this application's own store, for a deployment whose provider
/// does not carry them. Assignments are written by SQL or a seed: there is no API for them.
/// </summary>
public interface IRoleAssignmentQueries
{
    /// <returns>The owner's role names, in ordinal order; empty when none is assigned.</returns>
    Task<IReadOnlyList<string>> ListRolesAsync(UserId userId, CancellationToken cancellationToken = default);
}
