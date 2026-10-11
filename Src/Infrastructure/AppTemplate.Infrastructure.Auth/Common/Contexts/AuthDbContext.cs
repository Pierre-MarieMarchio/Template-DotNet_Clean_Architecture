using AppTemplate.Infrastructure.Auth.Features.Auth.Configurations;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Common.Contexts;

/// <summary>Everything this module stores: ASP.NET Identity's tables, the refresh-token grants and
/// the data protection key ring, in the <see cref="IdentitySchema"/> schema with a migrations
/// history of its own (docs/ARCHITECTURE.md argues why two contexts on one database). <b>This class
/// is the module's model composition root</b>, the one type in its <c>Common/</c> allowed to name a
/// feature. A context instance is a unit of work, scoped to one request and disposed by the
/// container; staged changes are committed by exactly one <c>SaveChangesAsync</c>, through the unit
/// of work this module registers, and the services around it borrow the context and never commit. It
/// shares no transaction with the business context: two contexts on one connection string are two
/// units of work, so a write here and a write there are two commits.</summary>
/// <param name="options">The options the container or the design-time factory built.</param>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>The schema every table in this context lives in.</summary>
    public const string IdentitySchema = "identity";

    /// <summary>
    /// This context's own history table. It carries the framework's usual name inside
    /// <see cref="IdentitySchema"/> rather than a second name in the default schema, so the two
    /// histories on this database are told apart by the same thing that tells their tables apart.
    /// </summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    /// <summary>The schema the migrations history table lives in.</summary>
    public const string MigrationsHistorySchema = IdentitySchema;

    /// <summary>
    /// Refresh-token grants. Internal, like the row type itself: the table is reached only through
    /// <see cref="Features.Auth.Tables.IRefreshTokenTable"/>, and the policy for how a grant is
    /// hashed, rotated and revoked lives in this module.
    /// </summary>
    internal DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    internal DbSet<SecurityEventRecord> SecurityEvents => Set<SecurityEventRecord>();

    /// <summary>
    /// Sessions: one row per sign-in, holding what its owner lists it by — see
    /// <see cref="Features.Auth.Tables.IRefreshTokenFamilyTable"/> for the boundary the module
    /// reaches it through.
    /// </summary>
    internal DbSet<RefreshTokenFamilyRecord> RefreshTokenFamilies => Set<RefreshTokenFamilyRecord>();

    /// <summary>
    /// The key ring <see cref="IDataProtectionKeyContext"/> requires. Public, unlike the set above:
    /// the ASP.NET Core data-protection system reads and writes it directly through that interface,
    /// so nothing about it can be internal to this assembly.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Applies this module's twelve configurations.</summary>
    /// <param name="builder">The model being built.</param>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        // Named one by one rather than discovered by scanning the assembly: twelve lines the compiler
        // checks, instead of a reflection call that silently maps nothing when a configuration class
        // is renamed or moved. No default schema is set — every table names its own.
        builder.ApplyConfiguration(new AppUserConfiguration());
        builder.ApplyConfiguration(new AppRoleConfiguration());
        builder.ApplyConfiguration(new RefreshTokenConfiguration());
        builder.ApplyConfiguration(new RefreshTokenFamilyConfiguration());
        builder.ApplyConfiguration(new SecurityEventConfiguration());
        builder.ApplyConfiguration(new UserPasskeyConfiguration());
        builder.ApplyConfiguration(new UserRoleConfiguration());
        builder.ApplyConfiguration(new UserClaimConfiguration());
        builder.ApplyConfiguration(new UserLoginConfiguration());
        builder.ApplyConfiguration(new RoleClaimConfiguration());
        builder.ApplyConfiguration(new UserTokenConfiguration());
        builder.ApplyConfiguration(new DataProtectionKeyConfiguration());
    }
}
