using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Configurations;

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEventRecord>
{
    /// <summary>The longest textual form an IPv6 address takes, IPv4-mapped tail included.</summary>
    internal const int IpAddressLength = 45;

    /// <summary>Longer user agents are cut, not refused: the header is the caller's to inflate.</summary>
    internal const int UserAgentLength = 512;

    internal const int KindLength = 64;

    internal const int DetailLength = 64;

    public void Configure(EntityTypeBuilder<SecurityEventRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SecurityEvents", AuthDbContext.IdentitySchema);

        builder.HasKey(securityEvent => securityEvent.Id);

        // The id is a UUIDv7 the model creates, so the database must not try to generate one.
        builder.Property(securityEvent => securityEvent.Id).ValueGeneratedNever();

        // Stored by name, so a row stays readable when the enum gains a member in the middle.
        builder.Property(securityEvent => securityEvent.Kind)
            .HasConversion<string>()
            .HasMaxLength(KindLength)
            .IsRequired();

        builder.Property(securityEvent => securityEvent.IpAddress).HasMaxLength(IpAddressLength);
        builder.Property(securityEvent => securityEvent.UserAgent).HasMaxLength(UserAgentLength);
        builder.Property(securityEvent => securityEvent.Detail).HasMaxLength(DetailLength);

        // Every read is one account's events, newest first.
        builder.HasIndex(securityEvent => new { securityEvent.UserId, securityEvent.OccurredAt });

        // The retention purge is a range scan over this column, across every account.
        builder.HasIndex(securityEvent => securityEvent.OccurredAt);
    }
}
