using System.Buffers.Text;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Features.AccessKeys.Adapters.Services;

public sealed class AccessKeysServicePersistenceTests(GrantTableFixture fixture) : IClassFixture<GrantTableFixture>
{
    private static readonly string[] _read = [AccessKeyPolicy.ReadScope];

    private static readonly string _accessKeys = DatabaseServer.Table(AuthDbContext.IdentitySchema, "AccessKeys");

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnIssuedKey_IsStoredAsItsHashAndAPrefix_NeverItself()
    {
        var owner = await fixture.CreateUserAsync(TestToken);

        var issued = await IssueAsync(owner.Id);

        issued.Key.ShouldStartWith(AccessKeyPolicy.KeyPrefix);
        AccessKeyPolicy.IsWellFormed(issued.Key).ShouldBeTrue();
        issued.Prefix.ShouldBe(issued.Key[..AccessKeyPolicy.DisplayPrefixLength]);

        (await ScalarAsync($"""SELECT "KeyHash" FROM {_accessKeys} WHERE "Id" = @id""", ("id", issued.Id)))
            .ShouldBe(Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(issued.Key))));
        (await CountAsync(
            $"""
            SELECT count(*) FROM {_accessKeys}
            WHERE "KeyHash" = @key OR "Prefix" = @key OR "Name" = @key OR "Scopes" = @key
            """,
            ("key", issued.Key))).ShouldBe(0, "the key itself is in no column");
    }

    [Fact]
    public async Task AKey_IsFoundByItself_NotByWhatIsStored()
    {
        var owner = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(owner.Id);
        string stored = Convert.ToString(
            await ScalarAsync($"""SELECT "KeyHash" FROM {_accessKeys} WHERE "Id" = @id""", ("id", issued.Id)),
            CultureInfo.InvariantCulture)!;

        await using var scope = fixture.Services.CreateAsyncScope();
        var accessKeys = scope.ServiceProvider.GetRequiredService<IAccessKeysService>();

        var grant = await accessKeys.FindAsync(issued.Key, TestToken);
        grant.ShouldNotBeNull();
        grant.AccessKeyId.ShouldBe(issued.Id);
        grant.UserId.ShouldBe(owner.Id);
        grant.Scopes.ShouldBe(_read);

        (await accessKeys.FindAsync(stored, TestToken)).ShouldBeNull(
            "a copy of the table must not authenticate: the stored hash is not a key.");
    }

    [Fact]
    public async Task TheOwnersLiveKeys_AreTheirsAlone_NeitherRevokedNorExpired_NewestFirst()
    {
        var owner = await fixture.CreateUserAsync(TestToken);
        var other = await fixture.CreateUserAsync(TestToken);

        var older = await IssueAsync(owner.Id);
        var newer = await IssueAsync(owner.Id, DateTimeOffset.UtcNow.AddDays(1));
        var revoked = await IssueAsync(owner.Id);
        await IssueAsync(owner.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
        await IssueAsync(other.Id);

        await using var scope = fixture.Services.CreateAsyncScope();
        var accessKeys = scope.ServiceProvider.GetRequiredService<IAccessKeysService>();
        (await accessKeys.RevokeAsync(owner.Id, revoked.Id, TestToken)).ShouldBeTrue();

        var live = await accessKeys.ListLiveAsync(owner.Id, TestToken);

        live.Select(accessKey => accessKey.Id).ShouldBe([newer.Id, older.Id]);
        (await accessKeys.CountLiveAsync(owner.Id, TestToken)).ShouldBe(2);
        live[0].Prefix.ShouldBe(newer.Prefix);
        live[0].Scopes.ShouldBe(_read);
    }

    [Fact]
    public async Task AnotherOwnersKey_IsNotRevoked_AndAKeyIsRevokedOnce()
    {
        var owner = await fixture.CreateUserAsync(TestToken);
        var other = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(owner.Id);

        await using var scope = fixture.Services.CreateAsyncScope();
        var accessKeys = scope.ServiceProvider.GetRequiredService<IAccessKeysService>();

        (await accessKeys.RevokeAsync(other.Id, issued.Id, TestToken)).ShouldBeFalse();
        (await accessKeys.FindAsync(issued.Key, TestToken))!.RevokedAt.ShouldBeNull();

        (await accessKeys.RevokeAsync(owner.Id, issued.Id, TestToken)).ShouldBeTrue();
        (await accessKeys.RevokeAsync(owner.Id, issued.Id, TestToken)).ShouldBeFalse();
        (await accessKeys.FindAsync(issued.Key, TestToken))!.RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AnExpiredKey_IsNoLongerRevocable()
    {
        var owner = await fixture.CreateUserAsync(TestToken);
        var expired = await IssueAsync(owner.Id, DateTimeOffset.UtcNow.AddSeconds(-1));

        await using var scope = fixture.Services.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<IAccessKeysService>()
            .RevokeAsync(owner.Id, expired.Id, TestToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task AUse_IsRecordedAtMostOncePerResolution()
    {
        var owner = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(owner.Id);
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        await using var scope = fixture.Services.CreateAsyncScope();
        var accessKeys = scope.ServiceProvider.GetRequiredService<IAccessKeysService>();

        await accessKeys.RecordUseAsync(issued.Id, first, TestToken);
        (await accessKeys.FindAsync(issued.Key, TestToken))!.LastUsedAt.ShouldBe(first);

        await accessKeys.RecordUseAsync(issued.Id, first + AccessKeyPolicy.LastUseResolution - TimeSpan.FromSeconds(1), TestToken);
        (await accessKeys.FindAsync(issued.Key, TestToken))!.LastUsedAt.ShouldBe(first);

        await accessKeys.RecordUseAsync(issued.Id, first + AccessKeyPolicy.LastUseResolution, TestToken);
        (await accessKeys.FindAsync(issued.Key, TestToken))!.LastUsedAt.ShouldBe(first + AccessKeyPolicy.LastUseResolution);
    }

    [Fact]
    public async Task DeletingTheOwner_DeletesTheirKeys()
    {
        var owner = await fixture.CreateUserAsync(TestToken);
        await IssueAsync(owner.Id);

        await ExecuteAsync(
            $"""DELETE FROM {DatabaseServer.Table(AuthDbContext.IdentitySchema, "User")} WHERE "Id" = @id""",
            ("id", owner.Id));

        (await CountAsync($"""SELECT count(*) FROM {_accessKeys} WHERE "UserId" = @id""", ("id", owner.Id)))
            .ShouldBe(0);
    }

    private async Task<IssuedAccessKey> IssueAsync(Guid userId, DateTimeOffset? expiresAt = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IAccessKeysService>()
            .IssueAsync(userId, "CI", _read, expiresAt, TestToken);
    }

    private async Task<int> CountAsync(string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt32(await ScalarAsync(sql, parameters), CultureInfo.InvariantCulture);

    private async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await using var command = await CommandAsync(scope, sql, parameters);

        return await command.ExecuteScalarAsync(TestToken);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await using var command = await CommandAsync(scope, sql, parameters);

        await command.ExecuteNonQueryAsync(TestToken);
    }

    private static async Task<DbCommand> CommandAsync(
        AsyncServiceScope scope,
        string sql,
        (string Name, object Value)[] parameters)
    {
        var connection = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(TestToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
