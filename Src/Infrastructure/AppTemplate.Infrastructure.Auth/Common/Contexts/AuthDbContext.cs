//#if (accessKeys)
using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Models;
//#endif
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Common.Contexts;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IDataProtectionKeyContext
{
    public const string IdentitySchema = "identity";

    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public const string MigrationsHistorySchema = IdentitySchema;

    public static ContextMigrations Migrations { get; } = new(
        typeof(AuthDbContext).Assembly.GetName().Name!,
        MigrationsHistoryTableName,
        MigrationsHistorySchema);

    internal DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    internal DbSet<SecurityEventRecord> SecurityEvents => Set<SecurityEventRecord>();

    internal DbSet<RefreshTokenFamilyRecord> RefreshTokenFamilies => Set<RefreshTokenFamilyRecord>();

    internal DbSet<SpentPasskeyCeremonyRecord> SpentPasskeyCeremonies => Set<SpentPasskeyCeremonyRecord>();

    //#if (accessKeys)
    internal DbSet<AccessKeyRecord> AccessKeys => Set<AccessKeyRecord>();

    //#endif
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.ApplyDatabaseConventions();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.ApplyConfiguration(new AppUserConfiguration());
        builder.ApplyConfiguration(new AppRoleConfiguration());
        builder.ApplyConfiguration(new RefreshTokenConfiguration());
        builder.ApplyConfiguration(new RefreshTokenFamilyConfiguration());
        builder.ApplyConfiguration(new SecurityEventConfiguration());
        builder.ApplyConfiguration(new UserPasskeyConfiguration());
        builder.ApplyConfiguration(new SpentPasskeyCeremonyConfiguration());
        //#if (accessKeys)
        builder.ApplyConfiguration(new AccessKeyConfiguration());
        //#endif
        builder.ApplyConfiguration(new UserRoleConfiguration());
        builder.ApplyConfiguration(new UserClaimConfiguration());
        builder.ApplyConfiguration(new UserLoginConfiguration());
        builder.ApplyConfiguration(new RoleClaimConfiguration());
        builder.ApplyConfiguration(new UserTokenConfiguration());
        builder.ApplyConfiguration(new DataProtectionKeyConfiguration());
    }
}
