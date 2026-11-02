namespace AppTemplate.Client.Common.Sessions;

public sealed class CookieRefreshTokenStore : IRefreshTokenStore
{
    public RefreshTokenMode Mode => RefreshTokenMode.Cookie;

    public ValueTask<string?> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

    public ValueTask WriteAsync(string? refreshToken, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
