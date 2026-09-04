using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppTemplate.Infrastructure.Persistence.Common.Contexts;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the web host, so migrations can be
/// generated and applied from this project alone:
/// <code>
/// dotnet tool restore
/// dotnet ef migrations add &lt;Name&gt; --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence --output-dir Migrations
/// </code>
/// The history table is configured here as well as at runtime. If it were configured only at
/// runtime, the tool would record migrations somewhere else and the two would disagree about what
/// has been applied.
/// </summary>
/// <remarks>
/// Public because it is instantiated by the <c>dotnet ef</c> tool from outside this assembly.
/// Reads <c>ConnectionStrings__Default</c> from the environment and falls back to the local
/// docker-compose database — see <see cref="DefaultConnectionString.ResolveForDesignTime"/>.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                DefaultConnectionString.ResolveForDesignTime(),
                npgsql => npgsql.MigrationsHistoryTable(
                    AppDbContext.MigrationsHistoryTableName,
                    AppDbContext.MigrationsHistorySchema))
            .Options;

        return new AppDbContext(options);
    }
}
