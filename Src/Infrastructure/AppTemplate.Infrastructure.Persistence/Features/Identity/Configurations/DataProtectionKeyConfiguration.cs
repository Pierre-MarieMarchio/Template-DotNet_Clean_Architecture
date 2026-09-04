using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Configurations;

/// <summary>
/// Mapping for the key ring the identity module persists its token-provider keys to, so a
/// confirmation or reset token minted by one instance stays valid on another.
/// </summary>
internal sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DataProtectionKeys", AppDbContext.IdentitySchema);
    }
}
