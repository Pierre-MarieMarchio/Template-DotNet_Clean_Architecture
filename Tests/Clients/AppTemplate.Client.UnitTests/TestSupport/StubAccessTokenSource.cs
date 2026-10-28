using AppTemplate.Client.Common.Sessions;

namespace AppTemplate.Client.UnitTests.TestSupport;

internal sealed class StubAccessTokenSource : IAccessTokenSource
{
    public string? AccessToken { get; set; }

    public ValueTask<string?> AccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(AccessToken);
}
