using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppTemplate.Infrastructure.Persistence.Common.Contexts;

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
