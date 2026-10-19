using AppTemplate.Application.Features.ExternalIdentity.Model.Policies;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Rows.Configurations;

/// <summary>
/// Mapping for a local role assignment. The key is the pair itself, so assigning a role twice is a
/// key violation rather than a duplicate row, and the owner leads it, which is how it is read.
/// </summary>
internal sealed class RoleAssignmentRecordConfiguration : IEntityTypeConfiguration<RoleAssignmentRecord>
{
    public void Configure(EntityTypeBuilder<RoleAssignmentRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RoleAssignments", AppDbContext.ExternalIdentitySchema);

        builder.HasKey(assignment => new { assignment.UserId, assignment.Role });

        builder.Property(assignment => assignment.Role)
            .HasMaxLength(ExternalIdentityPolicy.RoleMaxLength)
            .IsRequired();
    }
}
