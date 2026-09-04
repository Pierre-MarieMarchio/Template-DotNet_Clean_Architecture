using AppTemplate.Infrastructure.Identity.PasswordReset;

namespace AppTemplate.Infrastructure.Identity.EmailChange;

/// <summary>
/// The name this module registers <see cref="EmailChangeTokenProvider"/> under, and the value
/// <c>IdentityOptions.Tokens.ChangeEmailTokenProvider</c> is pointed at.
/// <para>
/// ASP.NET Identity's own default for that setting is <c>"Default"</c> — the same provider name email
/// confirmation resolves to — so leaving it there would give an email-change link the confirmation
/// link's lifespan (see
/// <see cref="AppTemplate.Infrastructure.Identity.Accounts.IdentityTokenOptions"/>) instead of its
/// own, shorter one. See <see cref="PasswordResetTokenProviderName"/> for the same reasoning applied
/// there first.
/// </para>
/// </summary>
internal static class EmailChangeTokenProviderName
{
    public const string Value = "EmailChange";
}
