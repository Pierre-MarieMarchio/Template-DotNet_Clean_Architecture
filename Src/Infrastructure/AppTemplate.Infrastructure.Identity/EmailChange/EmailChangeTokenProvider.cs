using AppTemplate.Infrastructure.Identity.PasswordReset;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.EmailChange;

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
