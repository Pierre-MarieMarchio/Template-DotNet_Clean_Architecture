namespace AppTemplate.Client.Common.Sessions;

public interface IAccessTokenSource
{
    ValueTask<string?> AccessTokenAsync(CancellationToken cancellationToken);
}
