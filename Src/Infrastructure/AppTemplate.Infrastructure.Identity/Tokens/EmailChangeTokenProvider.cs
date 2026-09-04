using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Tokens;

/// <summary>
/// The name this module registers <see cref="EmailChangeTokenProvider"/> under, and the value
/// <c>IdentityOptions.Tokens.ChangeEmailTokenProvider</c> is pointed at.
/// <para>
/// ASP.NET Identity's own default for that setting is <c>"Default"</c> — the same provider name email
/// confirmation resolves to — so leaving it there would give an email-change link the confirmation
/// link's lifespan (see
/// <see cref="AppTemplate.Infrastructure.Identity.Options.IdentityTokenOptions"/>) instead of its
/// own, shorter one. See <see cref="PasswordResetTokenProviderName"/> for the same reasoning applied
/// there first.
/// </para>
/// </summary>
internal static class EmailChangeTokenProviderName
{
    public const string Value = "EmailChange";
}

/// <summary>
/// A distinct <see cref="DataProtectionTokenProviderOptions"/> subtype, for the reason given on
/// <see cref="PasswordResetTokenProviderOptions"/>.
/// </summary>
internal sealed class EmailChangeTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailChangeTokenProviderOptions() => Name = "EmailChangeTokenProvider";
}

/// <summary>
/// <see cref="DataProtectorTokenProvider{TUser}"/> registered under <see cref="EmailChangeTokenProviderName"/>,
/// configured from <see cref="EmailChangeTokenProviderOptions"/> rather than the unnamed
/// <see cref="DataProtectionTokenProviderOptions"/> every other default provider shares.
/// </summary>
internal sealed class EmailChangeTokenProvider : DataProtectorTokenProvider<AppUser>
{
    public EmailChangeTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<EmailChangeTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<AppUser>> logger)
        : base(dataProtectionProvider, new OptionsWrapper<DataProtectionTokenProviderOptions>(options.Value), logger)
    {
    }
}
