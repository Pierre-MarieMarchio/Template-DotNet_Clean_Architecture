using AppTemplate.Infrastructure.Auth.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Migrations;

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
            + "The launcher's migration-add targets the business context only, so run 'dotnet ef migrations add <Name>' "
            + "with the Auth project on both --project and --startup-project (docs/guides/migrations.md) "
            + "so the schema change ships with the code that needs it.");
    }
}
