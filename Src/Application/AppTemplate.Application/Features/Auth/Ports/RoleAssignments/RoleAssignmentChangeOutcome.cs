namespace AppTemplate.Application.Features.Auth.Ports.RoleAssignments;

/// <param name="RejectionMessage">
/// Names the role or describes why the store refused, so it is safe to return verbatim. Set only for
/// <see cref="RoleAssignmentChangeStatus.Rejected"/>.
/// </param>
public sealed record RoleAssignmentChangeOutcome(RoleAssignmentChangeStatus Status, string? RejectionMessage = null)
{
    public static RoleAssignmentChangeOutcome Applied { get; } = new(RoleAssignmentChangeStatus.Applied);

    public static RoleAssignmentChangeOutcome NoSuchAccount { get; } = new(RoleAssignmentChangeStatus.NoSuchAccount);

    public static RoleAssignmentChangeOutcome Rejected(string message) =>
        new(RoleAssignmentChangeStatus.Rejected, message);
}
