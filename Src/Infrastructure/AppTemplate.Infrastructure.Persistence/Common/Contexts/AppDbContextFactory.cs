using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppTemplate.Infrastructure.Persistence.Common.Contexts;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseDatabase(DefaultConnectionString.ResolveForDesignTime(), AppDbContext.Migrations)
            .Options;

        return new AppDbContext(options);
    }
}
