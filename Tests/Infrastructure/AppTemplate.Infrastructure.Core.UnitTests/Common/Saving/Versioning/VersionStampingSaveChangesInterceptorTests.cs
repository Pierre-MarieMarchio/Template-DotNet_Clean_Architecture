using AppTemplate.Infrastructure.Core.Common.Saving.Versioning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Saving.Versioning;

public sealed class VersionStampingSaveChangesInterceptorTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(Token);

        await using var context = new VersionedContext(_connection);
        await context.Database.EnsureCreatedAsync(Token);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task AnAddedRow_StartsAtVersionOne()
    {
        var row = await AddAsync("first");

        row.Version.ShouldBe(1u);
        (await StoredVersionAsync(row.Id)).ShouldBe(1u);
    }

    [Fact]
    public async Task EveryUpdate_MovesTheVersionOnByOne()
    {
        var row = await AddAsync("first");

        await using (var context = new VersionedContext(_connection))
        {
            var stored = await context.Rows.SingleAsync(candidate => candidate.Id == row.Id, Token);
            stored.Name = "second";
            await context.SaveChangesAsync(Token);

            stored.Version.ShouldBe(2u);
        }

        (await StoredVersionAsync(row.Id)).ShouldBe(2u);
    }

    [Fact]
    public async Task AnUpdateFromAStaleVersion_IsAConcurrencyConflict()
    {
        var row = await AddAsync("first");

        await using var first = new VersionedContext(_connection);
        await using var second = new VersionedContext(_connection);
        var mine = await first.Rows.SingleAsync(candidate => candidate.Id == row.Id, Token);
        var theirs = await second.Rows.SingleAsync(candidate => candidate.Id == row.Id, Token);

        theirs.Name = "theirs";
        await second.SaveChangesAsync(Token);

        mine.Name = "mine";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(async () => await first.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task AnUnchangedRow_KeepsItsVersion()
    {
        var row = await AddAsync("first");

        await using (var context = new VersionedContext(_connection))
        {
            await context.Rows.SingleAsync(candidate => candidate.Id == row.Id, Token);
            await context.SaveChangesAsync(Token);
        }

        (await StoredVersionAsync(row.Id)).ShouldBe(1u);
    }

    [Fact]
    public async Task ARowWithoutAVersionToken_IsLeftAlone()
    {
        await using var context = new VersionedContext(_connection);
        var unversioned = new UnversionedRow { Id = Guid.CreateVersion7(), Version = 7 };

        context.Unversioned.Add(unversioned);
        await context.SaveChangesAsync(Token);

        unversioned.Version.ShouldBe(7u);
    }

    private async Task<VersionedRow> AddAsync(string name)
    {
        await using var context = new VersionedContext(_connection);
        var row = new VersionedRow { Id = Guid.CreateVersion7(), Name = name };

        context.Rows.Add(row);
        await context.SaveChangesAsync(Token);

        return row;
    }

    private async Task<uint> StoredVersionAsync(Guid id)
    {
        await using var context = new VersionedContext(_connection);

        return await context.Rows.Where(row => row.Id == id).Select(row => row.Version).SingleAsync(Token);
    }

    private sealed class VersionedRow
    {
        public Guid Id { get; set; }

        public required string Name { get; set; }

        public uint Version { get; set; }
    }

    private sealed class UnversionedRow
    {
        public Guid Id { get; set; }

        public uint Version { get; set; }
    }

    private sealed class VersionedContext(SqliteConnection connection) : DbContext(
        new DbContextOptionsBuilder<VersionedContext>()
            .UseSqlite(connection)
            .AddInterceptors(new VersionStampingSaveChangesInterceptor())
            .Options)
    {
        public DbSet<VersionedRow> Rows => Set<VersionedRow>();

        public DbSet<UnversionedRow> Unversioned => Set<UnversionedRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<VersionedRow>().Property(row => row.Version).IsConcurrencyToken();
        }
    }
}
