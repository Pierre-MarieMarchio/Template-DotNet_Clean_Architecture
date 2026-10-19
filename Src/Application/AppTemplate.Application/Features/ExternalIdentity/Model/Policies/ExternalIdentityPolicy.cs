namespace AppTemplate.Application.Features.ExternalIdentity.Model.Policies;

/// <summary>
/// The size limits on what a provider's token contributes to its mapping row, declared once and read
/// by the host that refuses an oversized token, by the service and by the column definitions.
/// </summary>
public static class ExternalIdentityPolicy
{
    /// <summary>
    /// The longest issuer. An issuer is a URL the deployment configures, so this is generous; it is
    /// bounded because it leads the unique index that keys the mapping.
    /// </summary>
    public const int IssuerMaxLength = 512;

    /// <summary>The longest subject: OpenID Connect Core caps <c>sub</c> at 255 ASCII characters.</summary>
    public const int SubjectMaxLength = 255;

    /// <summary>The longest email address kept; a longer one is not kept at all rather than cut.</summary>
    public const int EmailMaxLength = 320;

    /// <summary>The longest display name kept; a longer one is shortened, being only a greeting.</summary>
    public const int DisplayNameMaxLength = 256;

    /// <summary>The longest role name a local assignment may hold.</summary>
    public const int RoleMaxLength = 256;
}
