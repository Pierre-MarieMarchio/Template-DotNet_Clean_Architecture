namespace AppTemplate.Presentation.Core.Common.Security;

public sealed class AuthenticationExternalOptions
{
    public const string SectionName = "Authentication:External";

    public string Authority { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    public string SubjectClaim { get; set; } = "sub";

    public string EmailClaim { get; set; } = "email";

    public string NameClaim { get; set; } = "name";

    public RolesOptions Roles { get; set; } = new();

    public sealed class RolesOptions
    {
        public RoleSource Source { get; set; } = RoleSource.Local;

        public string? ClaimPath { get; set; }

        public string AdministratorRole { get; set; } = "Admin";
    }

    public enum RoleSource
    {
        Claim = 1,

        Local = 2,
    }
}
