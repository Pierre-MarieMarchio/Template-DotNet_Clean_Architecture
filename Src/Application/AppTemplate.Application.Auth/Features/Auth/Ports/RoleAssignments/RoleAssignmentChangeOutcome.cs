namespace AppTemplate.Application.Auth.Features.Auth.Ports.RoleAssignments;

/// <param name="RejectionMessage">
/// Names the role or describes why the store refused, so it is safe to return verbatim. Set only for
/// <see cref="RoleAssignmentChangeStatus.Rejected"/>.
/// </param>
public sealed record RoleAssignmentChangeOutcome(RoleAssignmentChangeStatus Status, string? RejectionMessage = null)
{
    /// <summary>The assignment now matches what was asked.</summary>
    public static RoleAssignmentChangeOutcome Applied { get; } = new(RoleAssignmentChangeStatus.Applied);

    /// <summary>No account has that id, so nothing was granted or revoked.</summary>
    public static RoleAssignmentChangeOutcome NoSuchAccount { get; } = new(RoleAssignmentChangeStatus.NoSuchAccount);

    /// <summary>The account exists; the store refused the change itself.</summary>
    public static RoleAssignmentChangeOutcome Rejected(string message) =>
        new(RoleAssignmentChangeStatus.Rejected, message);
}
