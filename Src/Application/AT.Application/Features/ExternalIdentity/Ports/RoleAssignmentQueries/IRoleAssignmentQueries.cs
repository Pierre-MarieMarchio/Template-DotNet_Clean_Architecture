using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Ports.RoleAssignmentQueries;

public interface IRoleAssignmentQueries
{
    Task<IReadOnlyList<string>> ListRolesAsync(UserId userId, CancellationToken cancellationToken = default);
}
