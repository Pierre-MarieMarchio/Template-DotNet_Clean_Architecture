namespace AppTemplate.Api.Features.Auth.Jobs;

internal sealed class AuthPurgeOptions
{
    public bool PurgeExpiredRefreshTokensEnabled { get; set; } = true;

    public bool PurgeExpiredSecurityEventsEnabled { get; set; } = true;
}
