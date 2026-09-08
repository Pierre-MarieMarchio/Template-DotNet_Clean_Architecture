using AppTemplate.Infrastructure.Auth.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Migrations;

/// <summary>
/// The same guarantee the business half has, for this half's own model: a change that never got a
/// migration is invisible until a deployment refuses to start.
/// <para>
/// It needs its own test because it has its own history. A context whose snapshot nobody compares
/// is a context nobody migrates, and with two histories on one database the failure does not
/// surface as a conflict — the other half's migrations apply cleanly and this half's tables simply
/// are not there.
/// </para>
/// </summary>
/// <remarks>
/// No database. <c>HasPendingModelChanges</c> compares the context's model against
/// <c>AuthDbContextModelSnapshot</c> — two models built in memory — so the connection string below
/// is never dialled. The provider is only there to give EF the same relational mapping the real one
/// has, because column types are part of what a snapshot records.
/// </remarks>
public sealed class PendingModelChangesTests
{
    [Fact]
    public void TheModel_IsFullyCoveredByTheMigrations()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql("Host=localhost;Database=never-opened;Username=none;Password=none")
            .Options;

        using var context = new AuthDbContext(options);

        context.Database.HasPendingModelChanges().ShouldBeFalse(
            "the model has changed since the last migration was generated. "
            + "Run 'dotnet ef migrations add <Name>' from the AppTemplate.Infrastructure.Auth project "
            + "so the schema change ships with the code that needs it.");
    }
}
