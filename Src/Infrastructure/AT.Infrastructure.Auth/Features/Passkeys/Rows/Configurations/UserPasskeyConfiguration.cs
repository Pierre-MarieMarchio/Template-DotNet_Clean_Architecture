using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Configurations;

internal sealed class UserPasskeyConfiguration : IEntityTypeConfiguration<IdentityUserPasskey<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserPasskey<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(passkey => passkey.CredentialId);

        builder.ToTable("UserPasskeys", AuthDbContext.IdentitySchema);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(passkey => passkey.UserId)
            .IsRequired();

        builder.OwnsOne(passkey => passkey.Data)
            .ToJson();
    }
}
