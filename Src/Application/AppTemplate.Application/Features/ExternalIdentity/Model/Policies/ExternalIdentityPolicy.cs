namespace AppTemplate.Application.Features.ExternalIdentity.Model.Policies;

public static class ExternalIdentityPolicy
{
    public const int IssuerMaxLength = 512;

    public const int SubjectMaxLength = 255;

    public const int EmailMaxLength = 320;

    public const int DisplayNameMaxLength = 256;

    public const int RoleMaxLength = 256;
}
