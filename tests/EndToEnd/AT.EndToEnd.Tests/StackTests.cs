using System.Net;
using Shouldly;
using Xunit;

namespace AppTemplate.EndToEnd.Tests;

public sealed class StackTests
{
    [Fact]
    public async Task TheComposedStack_MigratesAndReportsReady()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var api = Stack.Client(Stack.Api);

        using var response = await Wire.SendAsync(api, HttpMethod.Get, "health/ready", cancellationToken);

        (await Wire.ExpectAsync(response, HttpStatusCode.OK, cancellationToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task AnUnmappedRoute_IsRefusedWithTheApisProblemCode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var api = Stack.Client(Stack.Api);

        using var response = await Wire.SendAsync(api, HttpMethod.Get, Refusal.Path, cancellationToken);

        var problem = await Wire.ExpectJsonAsync(response, Refusal.Status, cancellationToken);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        Wire.Text(problem, "code").ShouldBe(Refusal.Code);
    }
}
