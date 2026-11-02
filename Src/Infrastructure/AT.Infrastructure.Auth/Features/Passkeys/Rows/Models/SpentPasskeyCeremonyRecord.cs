namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Models;

internal sealed class SpentPasskeyCeremonyRecord
{
    public required string StateHash { get; set; }

    public required DateTimeOffset ExpiresAt { get; set; }
}
