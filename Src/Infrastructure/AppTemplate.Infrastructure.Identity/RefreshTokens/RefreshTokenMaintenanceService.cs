using AppTemplate.Application.Features.Auth.Ports.RefreshTokenMaintenance;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Tables;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.RefreshTokens;

/// <summary>
/// <see cref="IRefreshTokenMaintenanceService"/> over <see cref="IRefreshTokenTable"/>. The only decision
/// made here is the retention window: a caller's <c>asOf</c> means "now", and this subtracts
/// <see cref="RefreshTokenOptions.RetentionInDays"/> before asking the table to delete, so a grant
/// stays around for a while after it expires rather than the instant it does.
/// </summary>
internal sealed class RefreshTokenMaintenanceService(
    IRefreshTokenTable table,
    IOptions<RefreshTokenOptions> options) : IRefreshTokenMaintenanceService
{
    public Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var cutoff = asOf.AddDays(-options.Value.RetentionInDays);

        return table.PurgeExpiredAsync(cutoff, cancellationToken);
    }
}
