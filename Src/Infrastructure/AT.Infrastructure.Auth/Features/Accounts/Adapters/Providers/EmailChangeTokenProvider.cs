using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Providers;

internal sealed class EmailChangeTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailChangeTokenProviderOptions() => Name = "EmailChangeTokenProvider";
}

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
