namespace AppTemplate.Client.Common.Sessions;

public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private string? _refreshToken;

    public RefreshTokenMode Mode => RefreshTokenMode.Body;

    public ValueTask<string?> ReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _refreshToken));

    public ValueTask WriteAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _refreshToken, refreshToken);

        return ValueTask.CompletedTask;
    }
}
