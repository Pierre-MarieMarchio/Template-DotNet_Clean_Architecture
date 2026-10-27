namespace AppTemplate.Client.Common.Sessions;

public interface IRefreshTokenStore
{
    RefreshTokenMode Mode { get; }

    ValueTask<string?> ReadAsync(CancellationToken cancellationToken);

    ValueTask WriteAsync(string? refreshToken, CancellationToken cancellationToken);
}
