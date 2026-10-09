using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.ExternalIdentity.Composition;
using AppTemplate.Api.UnitTests.Composition;
using AppTemplate.Application;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Application.Features.Notes.Services;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.ExternalIdentity;

/// <summary>
/// The external-authentication composition in a running pipeline: real bearer validation, real
/// mapping and a real owner-filtered service, with a key generated here standing in for the
/// provider's and SQLite for PostgreSQL. The provider's discovery document is replaced by the
/// configuration it would have served, which is the only part not exercised.
/// </summary>
public sealed class ExternalAuthenticationHostTests : IAsyncLifetime
{
    private const string _issuer = "https://idp.example.com/realms/app";
    private const string _audience = "app-api";

    private readonly RSA _providerRsa = RSA.Create(2048);
    private readonly RSA _strangerRsa = RSA.Create(2048);
    private DirectoryInfo _contentRoot = null!;
    private SqliteConnection _keeper = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private SecurityKey ProviderKey => new RsaSecurityKey(_providerRsa) { KeyId = "provider" };

    public async ValueTask InitializeAsync()
    {
        string database = SqliteSavingHost.AnInMemoryDatabase("external-host");
        _keeper = new SqliteConnection(database);
        await _keeper.OpenAsync(TestToken);

        // An empty content root, so the API's own appsettings copied beside this assembly are not read.
        _contentRoot = Directory.CreateTempSubdirectory("external-host-");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = _contentRoot.FullName,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Default"] = "Host=nowhere;Database=apptemplate;Username=u;Password=p",
            ["Authentication:External:Authority"] = _issuer,
            ["Authentication:External:Audience"] = _audience,
            ["Authentication:External:Roles:Source"] = "Claim",
            ["Authentication:External:Roles:ClaimPath"] = "realm_access.roles",
            ["Authentication:External:Roles:AdministratorRole"] = "app-admin",
        });

        builder.Logging.ClearProviders();

        var services = builder.Services;
        services.AddApiCallerIdentity();
        services.AddNotes();
        services.AddExternalAuthentication(builder.Configuration);
        SqliteSavingHost.OnSqlite<AppDbContext>(services, database);

        // What the discovery document would have said: this issuer, signing with this key.
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration { Issuer = _issuer };
            options.Configuration.SigningKeys.Add(ProviderKey);
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapGet("/me", (ICurrentUser caller) => caller.UserId?.Value.ToString() ?? "nobody");
        _app.MapPost("/notes", async (INoteService notes) =>
            (await notes.CreateAsync(new NoteContentDto("title", "body"))).Value.Value.Id.ToString());
        _app.MapGet("/notes/{id:guid}", async (Guid id, INoteService notes) =>
            (await notes.GetAsync(id)).IsSuccess ? Results.Ok() : Results.NotFound());
        _app.MapGet("/admin", () => "ok").RequireAuthorization(AuthorizationPolicies.Administrator);

        await _app.StartAsync(TestToken);
        await SqliteSavingHost.CreateSchemaAsync<AppDbContext>(_app.Services, _keeper, TestToken);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        await _keeper.DisposeAsync();
        _providerRsa.Dispose();
        _strangerRsa.Dispose();
        _contentRoot.Delete(recursive: true);
    }

    [Fact]
    public async Task AValidToken_IsServedAsItsMappedOwner_NeverAsItsSubject()
    {
        string subject = Guid.NewGuid().ToString();

        using var first = await GetAsync("/me", Token(subject));
        using var second = await GetAsync("/me", Token(subject));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        string owner = await first.Content.ReadAsStringAsync(TestToken);
        Guid.TryParse(owner, out _).ShouldBeTrue();
        owner.ShouldNotBe(subject);
        (await second.Content.ReadAsStringAsync(TestToken)).ShouldBe(owner);
    }

    [Fact]
    public async Task TwoSubjects_AreTwoOwners_ThatSeeOnlyTheirOwnNotes()
    {
        string alice = Token("alice");
        string bob = Token("bob");

        using var created = await SendAsync(HttpMethod.Post, "/notes", alice);
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        string noteId = await created.Content.ReadAsStringAsync(TestToken);

        using var byOwner = await GetAsync($"/notes/{noteId}", alice);
        using var byOther = await GetAsync($"/notes/{noteId}", bob);

        byOwner.StatusCode.ShouldBe(HttpStatusCode.OK);
        byOther.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NoToken_IsUnauthorized()
    {
        using var response = await GetAsync("/me", token: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenSignedByAnotherKey_IsUnauthorized()
    {
        using var response = await GetAsync(
            "/me",
            Token("alice", key: new RsaSecurityKey(_strangerRsa) { KeyId = "provider" }));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenForAnotherAudience_IsUnauthorized()
    {
        using var response = await GetAsync("/me", Token("alice", audience: "another-api"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenFromAnotherIssuer_IsUnauthorized()
    {
        using var response = await GetAsync("/me", Token("alice", issuer: "https://elsewhere.example.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnExpiredToken_IsUnauthorized()
    {
        using var response = await GetAsync("/me", Token("alice", expires: DateTime.UtcNow.AddMinutes(-5)));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AValidTokenWithoutASubject_IsUnauthorized_NotAnonymous()
    {
        using var response = await GetAsync("/me", Token(subject: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheAdministratorRoleAtTheClaimPath_PassesTheAdministratorPolicy()
    {
        using var response = await GetAsync("/admin", Token("alice", roles: ["app-admin"]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnotherRole_IsForbiddenTheAdministratorPolicy()
    {
        using var response = await GetAsync("/admin", Token("alice", roles: ["reader"]));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? token) => SendAsync(HttpMethod.Get, path, token);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path);

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return _client.SendAsync(request, TestToken);
    }

    private string Token(
        string? subject,
        string[]? roles = null,
        SecurityKey? key = null,
        string issuer = _issuer,
        string audience = _audience,
        DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["email"] = $"{subject ?? "nobody"}@example.com",
            ["realm_access"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["roles"] = roles ?? [] },
        };

        if (subject is not null)
        {
            claims["sub"] = subject;
        }

        DateTime notAfter = expires ?? DateTime.UtcNow.AddMinutes(5);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = notAfter.AddMinutes(-10),
            NotBefore = notAfter.AddMinutes(-10),
            Expires = notAfter,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? ProviderKey, SecurityAlgorithms.RsaSha256),
        });
    }
}
