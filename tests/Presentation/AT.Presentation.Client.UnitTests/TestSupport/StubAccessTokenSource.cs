using AppTemplate.Presentation.Client.Common.Sessions;

namespace AppTemplate.Presentation.Client.UnitTests.TestSupport;

internal sealed class StubAccessTokenSource : IAccessTokenSource
{
    public string? AccessToken { get; set; }

    public ValueTask<string?> AccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(AccessToken);
}
