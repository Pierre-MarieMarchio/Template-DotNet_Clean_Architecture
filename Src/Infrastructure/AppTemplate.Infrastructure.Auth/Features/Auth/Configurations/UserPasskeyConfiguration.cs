using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Configurations;

/// <summary>
/// The passkeys the accounts hold: one row per WebAuthn credential, its public key and signature
/// counter in an owned <c>Data</c> JSON column.
/// <para>
/// Everything here is explicit, where the sibling configurations only re-table a mapping the base
/// <c>IdentityDbContext</c> already makes: on this package band the base context declares the
/// <c>UserPasskeys</c> set but maps no entity behind it, so a passkey row would have no key and
/// no owner — the model refuses to build. The key, the cascade to the account, and the JSON
/// column are therefore stated here once, in the one place that owns the table.
/// </para>
/// </summary>
internal sealed class UserPasskeyConfiguration : IEntityTypeConfiguration<IdentityUserPasskey<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserPasskey<Guid>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(passkey => passkey.CredentialId);

        builder.ToTable("UserPasskeys", AuthDbContext.IdentitySchema);

        // Deleting an account deletes its passkeys with it, exactly as every other credential the
        // user store holds for it — an account's ways in should not outlive the account.
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(passkey => passkey.UserId)
            .IsRequired();

        // The credential's payload as one jsonb column: what a ceremony wrote, read back only as a
        // whole, and never queried field by field.
        builder.OwnsOne(passkey => passkey.Data)
            .ToJson();
    }
}
