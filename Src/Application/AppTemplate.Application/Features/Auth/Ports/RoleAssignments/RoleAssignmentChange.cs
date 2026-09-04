namespace AppTemplate.Application.Features.Auth.Ports.RoleAssignments;

/// <param name="RejectionMessage">
/// Names the role or describes why the store refused, so it is safe to return verbatim. Set only for
/// <see cref="RoleAssignmentChangeOutcome.Rejected"/>.
/// </param>
public sealed record RoleAssignmentChange(RoleAssignmentChangeOutcome Outcome, string? RejectionMessage = null)
{
    public static RoleAssignmentChange Applied { get; } = new(RoleAssignmentChangeOutcome.Applied);

    public static RoleAssignmentChange NoSuchAccount { get; } = new(RoleAssignmentChangeOutcome.NoSuchAccount);

    public static RoleAssignmentChange Rejected(string message) =>
        new(RoleAssignmentChangeOutcome.Rejected, message);
}
