using System.Net;
using Shouldly;
using Xunit;

namespace AppTemplate.EndToEnd.Tests;

public sealed class WebAppTests
{
    public static TheoryData<string> WebAppPorts => [.. Stack.WebAppPortSettings];

    [Theory]
    [MemberData(nameof(WebAppPorts))]
    public async Task AWebApp_ServesItsPageAndRelaysApiToTheApi(string portSetting)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var site = Stack.Client(Stack.WebApp(portSetting));

        using (var page = await Wire.SendAsync(site, HttpMethod.Get, string.Empty, cancellationToken))
        {
            (await Wire.ExpectAsync(page, HttpStatusCode.OK, cancellationToken)).ShouldContain("<html", Case.Insensitive);
        }

        using var relayed = await Wire.SendAsync(site, HttpMethod.Get, Refusal.Path, cancellationToken);

        var problem = await Wire.ExpectJsonAsync(relayed, Refusal.Status, cancellationToken);
        relayed.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        Wire.Text(problem, "code").ShouldBe(Refusal.Code);
    }
}
