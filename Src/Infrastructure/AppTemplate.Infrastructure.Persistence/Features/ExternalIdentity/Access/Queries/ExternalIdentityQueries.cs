using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Access.Queries;

/// <summary>The adapter for <see cref="IExternalIdentityQueries"/>: reads only, nothing tracked.</summary>
internal sealed class ExternalIdentityQueries(AppDbContext context) : IExternalIdentityQueries
{
    public async Task<ExternalIdentityProfile?> FindProfileAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        // An owner has one row per provider it has been known by — one, or a few after a change of
        // provider — so the newest is picked here rather than by an ORDER BY over an instant column,
        // which not every provider this model runs on can translate.
        var mappings = await context.ExternalIdentities
            .AsNoTracking()
            .Where(identity => identity.UserId == userId.Value)
            .Select(identity => new { identity.Email, identity.DisplayName, identity.LastSeenAt })
            .ToListAsync(cancellationToken);

        var newest = mappings.MaxBy(mapping => mapping.LastSeenAt);

        return newest is null ? null : new ExternalIdentityProfile(newest.Email, newest.DisplayName);
    }
}
