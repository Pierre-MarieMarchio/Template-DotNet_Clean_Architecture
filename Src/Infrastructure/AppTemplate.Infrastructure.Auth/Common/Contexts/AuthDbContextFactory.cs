using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppTemplate.Infrastructure.Auth.Common.Contexts;

public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
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
