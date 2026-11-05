namespace AppTemplate.Presentation.Client.Common.Sessions;

public interface IAccessTokenSource
{
    ValueTask<string?> AccessTokenAsync(CancellationToken cancellationToken);
}
