using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Migrations;

/// <summary>
/// The migrations in this repository are versioned by hand-written intent as much as by tooling, and
/// a model change that never got a migration is invisible until a deployment refuses to start. This
/// closes that gap at build time.
/// </summary>
/// <remarks>
/// No database. <c>HasPendingModelChanges</c> compares the context's model against
/// <c>AppDbContextModelSnapshot</c> — two models built in memory — so the connection string below is
/// never dialled. The provider is only there to give EF the same relational mapping the real one has,
/// because column types are part of what a snapshot records.
/// </remarks>
public sealed class PendingModelChangesTests
{
    [Fact]
    public void TheModel_IsFullyCoveredByTheMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never-opened;Username=none;Password=none")
            .Options;

        using var context = new AppDbContext(options);

        context.Database.HasPendingModelChanges().ShouldBeFalse(
            "the model has changed since the last migration was generated. "
            + "Run 'dotnet ef migrations add <Name>' from the AppTemplate.Infrastructure.Persistence project "
            + "so the schema change ships with the code that needs it.");
    }
}
