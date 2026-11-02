using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Migrations;

public sealed class PendingModelChangesTests
{
    [Fact]
    public void TheModel_IsFullyCoveredByTheMigrations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
            .Options;

        using var context = new AppDbContext(options);

        context.Database.HasPendingModelChanges().ShouldBeFalse(
            "the model has changed since the last migration was generated, or the project has none yet. "
            + "Without one, run 'dotnet run Tools/Tasks.cs bootstrap', which creates the initial migration; after a model change, "
            + "run 'dotnet run Tools/Tasks.cs migration-add <Name>' so the schema change ships with the code that needs it "
            + "(docs/guides/migrations.md).");
    }
}
