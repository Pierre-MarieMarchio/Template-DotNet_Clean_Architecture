using System.Net;
using System.Net.Http.Json;
//#if (notes)
using AppTemplate.Application.Features.Notes.Services;
//#endif
using AppTemplate.PresentationAppMcp.UnitTests.TestSupport;
//#if (notes)
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
//#endif
//#if (localAuth)
using ModelContextProtocol;
//#endif
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppMcp.UnitTests;

public sealed class McpAuthorizationTests
{
    //#if (localAuth)
    private const string _caller = TestKeys.ReadWrite;

    private const string _anotherCaller = TestKeys.Another;
    //#endif
    //#if (externalAuth)
    private static readonly string _caller = TestTokens.For("alice");

    private static readonly string _anotherCaller = TestTokens.For("bob");
    //#endif

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACallWithoutCredentials_IsRefusedBeforeAnyTool()
    {
        await using var host = new McpHost();
        using var http = host.CreateClient();

        using var response = await http.PostAsync("/mcp", ToolsList(), Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        //#if (externalAuth)
        response.Headers.WwwAuthenticate.ToString().ShouldContain(
            "resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"",
            Case.Sensitive,
            "The challenge names where a client learns which provider issues tokens for this server.");
        //#endif
    }

    [Fact]
    public async Task TheHealthEndpoint_AnswersAnyone()
    {
        await using var host = new McpHost();
        using var http = host.CreateClient();

        using var response = await http.GetAsync("/health", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ACaller_ListsTheToolsTheServerDeclares()
    {
        await using var host = new McpHost();
        await using var client = await host.ConnectAsync(_caller, Cancellation);

        var tools = await client.ListToolsAsync(cancellationToken: Cancellation);

        tools.Select(tool => tool.Name).ShouldBe(ToolCatalogue.Names, ignoreOrder: true);
    }

    [Fact]
    public async Task EveryTool_SaysWhetherItOnlyReads()
    {
        await using var host = new McpHost();
        await using var client = await host.ConnectAsync(_caller, Cancellation);

        var tools = await client.ListToolsAsync(cancellationToken: Cancellation);

        tools.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true).Select(tool => tool.Name)
            .ShouldBe(ToolCatalogue.ReadOnlyNames, ignoreOrder: true);
        tools.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint != true && tool.ProtocolTool.Annotations?.DestructiveHint != false).ShouldBeEmpty(
            "No tool deletes or overwrites, and a client asks its user before a tool that does not say so.");
    }
    //#if (externalAuth)

    [Fact]
    public async Task TheResourceMetadata_NamesThisServerAndTheProviderThatIssuesItsTokens()
    {
        await using var host = new McpHost();
        using var http = host.CreateClient();

        var metadata = await http.GetFromJsonAsync<Dictionary<string, object>>(
            "/.well-known/oauth-protected-resource/mcp",
            Cancellation);

        metadata.ShouldNotBeNull();
        metadata["resource"].ToString().ShouldBe(McpHost.Resource);
        metadata["authorization_servers"].ToString()!.ShouldContain(TestTokens.Issuer);
    }

    [Fact]
    public async Task ATokenIssuedForTheApi_IsRefused()
    {
        await using var host = new McpHost();
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For("alice", audience: TestTokens.ApiAudience));

        using var response = await http.PostAsync("/mcp", ToolsList(), Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
    //#endif
    //#if (localAuth)

    [Fact]
    public async Task ARevokedOrUnknownKey_IsRefused()
    {
        await using var host = new McpHost();
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", TestKeys.Revoked);

        using var response = await http.PostAsync("/mcp", ToolsList(), Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
    }

    [Fact]
    public async Task AKeyThatOnlyReads_SeesNoToolThatWrites()
    {
        await using var host = new McpHost();
        await using var client = await host.ConnectAsync(TestKeys.ReadOnly, Cancellation);

        var tools = await client.ListToolsAsync(cancellationToken: Cancellation);

        tools.Select(tool => tool.Name).ShouldBe(ToolCatalogue.ReadOnlyNames, ignoreOrder: true);
    }
    //#if (notes)

    [Fact]
    public async Task AKeyThatOnlyReads_IsRefusedAToolThatWrites()
    {
        await using var host = new McpHost(services => services.Replace(ServiceDescriptor.Scoped<INoteService, InMemoryNotes>()));
        await using var client = await host.ConnectAsync(TestKeys.ReadOnly, Cancellation);

        await Should.ThrowAsync<McpProtocolException>(() => client.CallToolAsync(
            "create_note",
            new Dictionary<string, object?> { ["title"] = "Groceries", ["body"] = "Milk" },
            cancellationToken: Cancellation).AsTask());
    }
    //#endif
    //#endif
    //#if (notes)

    [Fact]
    public async Task ARecordOfAnotherCaller_AnswersAsAnAbsentOne()
    {
        await using var host = new McpHost(services => services.Replace(ServiceDescriptor.Scoped<INoteService, InMemoryNotes>()));
        await using var owner = await host.ConnectAsync(_caller, Cancellation);
        await using var stranger = await host.ConnectAsync(_anotherCaller, Cancellation);

        var created = await owner.CallToolAsync(
            "create_note",
            new Dictionary<string, object?> { ["title"] = "Groceries", ["body"] = "Milk" },
            cancellationToken: Cancellation);
        string noteId = created.StructuredContent!.Value.GetProperty("id").GetString()!;

        var byOwner = await owner.CallToolAsync("get_note", new Dictionary<string, object?> { ["noteId"] = noteId }, cancellationToken: Cancellation);
        var byStranger = await stranger.CallToolAsync("get_note", new Dictionary<string, object?> { ["noteId"] = noteId }, cancellationToken: Cancellation);
        var absent = await stranger.CallToolAsync("get_note", new Dictionary<string, object?> { ["noteId"] = Guid.CreateVersion7() }, cancellationToken: Cancellation);

        byOwner.IsError.ShouldNotBe(true);
        byStranger.IsError.ShouldBe(true);
        byStranger.StructuredContent!.Value.GetProperty("code").GetString().ShouldBe(
            absent.StructuredContent!.Value.GetProperty("code").GetString(),
            "Another caller's note answers exactly as a note that does not exist, so a tool reveals nothing the caller does not own.");
    }
    //#endif

    private static JsonContent ToolsList() =>
        JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" });
}
