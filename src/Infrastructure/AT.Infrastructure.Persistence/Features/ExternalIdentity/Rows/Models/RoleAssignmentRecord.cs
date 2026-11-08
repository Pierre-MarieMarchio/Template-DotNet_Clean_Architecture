namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Rows.Models;

internal sealed class RoleAssignmentRecord
{
    public Guid UserId { get; set; }

    public string Role { get; set; } = string.Empty;
}
