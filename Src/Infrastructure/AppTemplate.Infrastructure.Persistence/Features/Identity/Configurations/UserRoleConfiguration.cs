using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Configurations;

/// <summary>
/// One of the five tables ASP.NET Identity contributes that this system does not model itself: role
/// assignments, user claims, external logins, role claims and per-user tokens.
/// <para>
/// They are configured, rather than left to convention, for one reason: to give each an explicit table
/// name and an explicit schema. Every table in this context names its own schema, so that a new mapping
/// cannot land in the wrong one by forgetting to say. There is nothing to read in any of the five beyond
/// that one line, which is why the other four inherit this summary rather than restate it.
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
