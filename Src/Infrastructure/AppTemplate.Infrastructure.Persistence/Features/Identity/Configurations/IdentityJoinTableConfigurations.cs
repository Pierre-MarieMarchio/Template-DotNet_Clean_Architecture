using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Configurations;

/// <summary>
/// The five tables ASP.NET Identity contributes that this system does not model itself: role
/// assignments, user claims, external logins, role claims and per-user tokens.
/// <para>
/// They are configured, rather than left to convention, for one reason: to give each an explicit table
/// name and an explicit schema. Every table in this context names its own schema, so that a new mapping
/// cannot land in the wrong one by forgetting to say. Grouped in a single file because there is nothing
/// to read in any of them beyond that one line.
/// </para>
/// </summary>
internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserRoles", AppDbContext.IdentitySchema);
    }
}

/// <inheritdoc cref="UserRoleConfiguration"/>
internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserClaims", AppDbContext.IdentitySchema);
    }
}

/// <inheritdoc cref="UserRoleConfiguration"/>
internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserLogins", AppDbContext.IdentitySchema);
    }
}

/// <inheritdoc cref="UserRoleConfiguration"/>
internal sealed class RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RoleClaims", AppDbContext.IdentitySchema);
    }
}

/// <inheritdoc cref="UserRoleConfiguration"/>
internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserTokens", AppDbContext.IdentitySchema);
    }
}
