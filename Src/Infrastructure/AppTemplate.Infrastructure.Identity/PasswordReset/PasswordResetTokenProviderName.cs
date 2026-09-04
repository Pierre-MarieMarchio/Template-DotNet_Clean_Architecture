namespace AppTemplate.Infrastructure.Identity.PasswordReset;

/// <summary>
/// The name this module registers <see cref="PasswordResetTokenProvider"/> under, and the value
/// <c>IdentityOptions.Tokens.PasswordResetTokenProvider</c> is pointed at.
/// <para>
/// ASP.NET Identity's own default for that setting is <c>"Default"</c> — the same provider name email
/// confirmation resolves to — so leaving it there would give a reset link the confirmation link's
/// lifespan (see <see cref="AppTemplate.Infrastructure.Identity.Accounts.IdentityTokenOptions"/>) instead of its
/// own, shorter one.
/// </para>
/// </summary>
internal static class PasswordResetTokenProviderName
{
    public const string Value = "PasswordReset";
}
