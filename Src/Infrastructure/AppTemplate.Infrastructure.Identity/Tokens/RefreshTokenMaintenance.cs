using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Infrastructure.Identity.Options;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Stores;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Tokens;

/// <summary>
/// <see cref="IRefreshTokenMaintenance"/> over <see cref="IRefreshTokenStore"/>. The only decision
/// made here is the retention window: <paramref name="asOf"/>'s caller means "now", and this
/// subtracts <see cref="RefreshTokenOptions.RetentionInDays"/> before asking the store to delete,
/// so a grant stays around for a while after it expires rather than the instant it does.
/// </summary>
internal sealed class RefreshTokenMaintenance(
    IRefreshTokenStore store,
    IOptions<RefreshTokenOptions> options) : IRefreshTokenMaintenance
{
    public Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var cutoff = asOf.AddDays(-options.Value.RetentionInDays);

        return store.PurgeExpiredAsync(cutoff, cancellationToken);
    }
}
