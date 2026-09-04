using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Providers;

/// <summary>
/// A distinct <see cref="DataProtectionTokenProviderOptions"/> subtype, so DI resolves an
/// <see cref="IOptions{TOptions}"/> instance of its own rather than the one <c>AddDefaultTokenProviders</c>
/// wires up for the <c>"Default"</c> provider that email confirmation also resolves to.
/// </summary>
internal sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions() => Name = "PasswordResetTokenProvider";
}

/// <summary>
/// <see cref="DataProtectorTokenProvider{TUser}"/> registered under <see cref="PasswordResetTokenProviderName"/>,
/// configured from <see cref="PasswordResetTokenProviderOptions"/> rather than the unnamed
/// <see cref="DataProtectionTokenProviderOptions"/> every other default provider shares.
/// </summary>
internal sealed class PasswordResetTokenProvider : DataProtectorTokenProvider<AppUser>
{
    public PasswordResetTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PasswordResetTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<AppUser>> logger)
        : base(dataProtectionProvider, new OptionsWrapper<DataProtectionTokenProviderOptions>(options.Value), logger)
    {
    }
}
