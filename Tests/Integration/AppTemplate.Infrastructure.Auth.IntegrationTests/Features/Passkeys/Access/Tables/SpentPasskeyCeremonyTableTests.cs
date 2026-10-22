using System.Buffers.Text;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;
using AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Features.Passkeys.Access.Tables;

public sealed class SpentPasskeyCeremonyTableTests(GrantTableFixture fixture) : IClassFixture<GrantTableFixture>
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoSimultaneousSpendsOfTheSameState_SpendItExactlyOnce()
    {
        string stateHash = AStateHash();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var rendezvous = new Rendezvous(participants: 2);

        bool[] spends = await Task.WhenAll(
            SpendAsync(stateHash, expiresAt, rendezvous),
            SpendAsync(stateHash, expiresAt, rendezvous));

        spends.Count(spent => spent).ShouldBe(
            1,
            "a ceremony state is single-use: of two simultaneous presentations exactly one may reach " +
            "the handler. Two winners means one captured state signs in twice.");
        (await CountAsync(stateHash)).ShouldBe(1);
    }

    [Fact]
    public async Task SpendingAStateAlreadySpent_IsRefused()
    {
        string stateHash = AStateHash();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        (await SpendAsync(stateHash, expiresAt)).ShouldBeTrue();
        (await SpendAsync(stateHash, expiresAt)).ShouldBeFalse();
    }

    [Fact]
    public async Task ASpend_IsDurableWithoutTheUnitOfWork()
    {
        string stateHash = AStateHash();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISpentPasskeyCeremonyTable>()
                .TrySpendAsync(stateHash, DateTimeOffset.UtcNow.AddMinutes(5), TestToken);

            scope.ServiceProvider.GetRequiredService<AuthDbContext>().ChangeTracker.HasChanges().ShouldBeFalse(
                "the spend bypasses the change tracker, so nothing is left staged for the request's " +
                "own commit to carry, or to retry.");
        }

        (await CountAsync(stateHash)).ShouldBe(1);
    }

    [Fact]
    public async Task PurgeExpiredAsync_RemovesOnlyWhatHasExpired_AndAnswersTheCount()
    {
        var cutoff = DateTimeOffset.UtcNow.AddYears(-10);
        string expired = AStateHash();
        string expiringAtTheCutoff = AStateHash();
        string live = AStateHash();

        await SpendAsync(expired, cutoff.AddMinutes(-1));
        await SpendAsync(expiringAtTheCutoff, cutoff);
        await SpendAsync(live, cutoff.AddMinutes(1));

        int purged;

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            purged = await scope.ServiceProvider.GetRequiredService<ISpentPasskeyCeremonyTable>()
                .PurgeExpiredAsync(cutoff, TestToken);
        }

        purged.ShouldBe(2);
        (await CountAsync(expired)).ShouldBe(0);
        (await CountAsync(expiringAtTheCutoff)).ShouldBe(0);
        (await CountAsync(live)).ShouldBe(1);
    }

    private async Task<bool> SpendAsync(string stateHash, DateTimeOffset expiresAt, Rendezvous? rendezvous = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var table = scope.ServiceProvider.GetRequiredService<ISpentPasskeyCeremonyTable>();

        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.OpenConnectionAsync(TestToken);

        if (rendezvous is not null)
        {
            await rendezvous.ArriveAsync(TestToken);
        }

        return await table.TrySpendAsync(stateHash, expiresAt, TestToken);
    }

    private async Task<int> CountAsync(string stateHash)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(TestToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""SELECT count(*) FROM {DatabaseServer.Table(AuthDbContext.IdentitySchema, "SpentPasskeyCeremonies")} WHERE "StateHash" = @stateHash""";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "stateHash";
        parameter.Value = stateHash;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync(TestToken), CultureInfo.InvariantCulture);
    }

    private static string AStateHash() =>
        Base64Url.EncodeToString(SHA256.HashData(RandomNumberGenerator.GetBytes(32)));
}
