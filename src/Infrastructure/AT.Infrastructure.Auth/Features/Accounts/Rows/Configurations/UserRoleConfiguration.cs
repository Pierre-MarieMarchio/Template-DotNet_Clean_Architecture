using AppTemplate.Infrastructure.Auth.Common.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Configurations;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserRoles", AuthDbContext.IdentitySchema);
    }
}
