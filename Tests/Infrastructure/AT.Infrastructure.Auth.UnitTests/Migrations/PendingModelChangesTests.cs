using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Contexts;
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
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
            .Options;

        using var context = new AuthDbContext(options);

        context.Database.HasPendingModelChanges().ShouldBeFalse(
            "the model has changed since the last migration was generated, or the project has none yet. "
            + "Without one, run 'dotnet run Tools/Tasks.cs bootstrap', which creates the initial migration; after a model change, "
            + "migration-add targets the business context only, so run 'dotnet ef migrations add <Name>' with the Auth project "
            + "on both --project and --startup-project so the schema change ships with the code that needs it "
            + "(docs/guides/migrations.md).");
    }
}
