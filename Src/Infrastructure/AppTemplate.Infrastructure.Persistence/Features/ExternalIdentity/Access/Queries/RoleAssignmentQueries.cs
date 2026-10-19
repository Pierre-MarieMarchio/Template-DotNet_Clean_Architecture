using AppTemplate.Application.Features.ExternalIdentity.Ports.RoleAssignmentQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Access.Queries;

/// <summary>The adapter for <see cref="IRoleAssignmentQueries"/>: reads only, nothing tracked.</summary>
internal sealed class RoleAssignmentQueries(AppDbContext context) : IRoleAssignmentQueries
{
    public async Task<IReadOnlyList<string>> ListRolesAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var roles = await context.RoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId.Value)
            .Select(assignment => assignment.Role)
            .ToListAsync(cancellationToken);

        roles.Sort(StringComparer.Ordinal);

        return roles;
    }
}
