namespace AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Models;

/// <summary>
/// One role held by one owner, read when the deployment takes roles from this store rather than from
/// the provider's token. Written by SQL or a seed; nothing in this application writes it.
/// </summary>
internal sealed class RoleAssignmentRecord
{
    public Guid UserId { get; set; }

    public string Role { get; set; } = string.Empty;
}
