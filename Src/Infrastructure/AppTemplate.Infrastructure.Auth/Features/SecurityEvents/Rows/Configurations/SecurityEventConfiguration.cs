using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Configurations;

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEventRecord>
{
    internal const int IpAddressLength = 45;

    internal const int UserAgentLength = 512;

    internal const int KindLength = 64;

    internal const int DetailLength = 64;

    public void Configure(EntityTypeBuilder<SecurityEventRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SecurityEvents", AuthDbContext.IdentitySchema);

        builder.HasKey(securityEvent => securityEvent.Id);

        builder.Property(securityEvent => securityEvent.Id).ValueGeneratedNever();

        builder.Property(securityEvent => securityEvent.Kind)
            .HasConversion<string>()
            .HasMaxLength(KindLength)
            .IsRequired();

        builder.Property(securityEvent => securityEvent.IpAddress).HasMaxLength(IpAddressLength);
        builder.Property(securityEvent => securityEvent.UserAgent).HasMaxLength(UserAgentLength);
        builder.Property(securityEvent => securityEvent.Detail).HasMaxLength(DetailLength);

        builder.HasIndex(securityEvent => new { securityEvent.UserId, securityEvent.OccurredAt });

        builder.HasIndex(securityEvent => securityEvent.OccurredAt);
    }
}
