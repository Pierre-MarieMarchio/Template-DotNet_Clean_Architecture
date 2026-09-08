using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppTemplate.Infrastructure.Auth.Common.Contexts;

/// <summary>
/// Lets <c>dotnet ef</c> build this module's model without starting a host, so its migrations can
/// be generated and applied from this project alone:
/// <code>
/// dotnet tool restore
/// dotnet ef migrations add &lt;Name&gt; --project Src/Infrastructure/AppTemplate.Infrastructure.Auth --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Auth --output-dir Migrations
/// </code>
/// The history table is configured here as well as at runtime. If it were configured only at
/// runtime, the tool would record migrations somewhere else and the two would disagree about what
/// has been applied — and with two contexts on one database, somewhere else is the other half's
/// history table.
/// </summary>
/// <remarks>
/// Public because it is instantiated by the <c>dotnet ef</c> tool from outside this assembly.
/// Reads <c>ConnectionStrings__Default</c> from the environment and falls back to the local
/// docker-compose database — see <see cref="DefaultConnectionString.ResolveForDesignTime"/>.
/// </remarks>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    /// <summary>Builds the context the tool scaffolds against.</summary>
    /// <param name="args">The tool's arguments, which this factory does not read.</param>
    /// <returns>A context pointed at the design-time connection string.</returns>
    public AuthDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(
                DefaultConnectionString.ResolveForDesignTime(),
                npgsql => npgsql.MigrationsHistoryTable(
                    AuthDbContext.MigrationsHistoryTableName,
                    AuthDbContext.MigrationsHistorySchema))
            .Options;

        return new AuthDbContext(options);
    }
}
