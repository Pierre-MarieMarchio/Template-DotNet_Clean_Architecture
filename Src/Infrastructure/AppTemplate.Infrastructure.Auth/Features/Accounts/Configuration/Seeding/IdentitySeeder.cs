using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Seeding;

internal sealed class IdentitySeeder(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentitySeedOptions> options,
    IHostEnvironment environment,
    IDateTimeProvider dateTimeProvider,
    ILogger<IdentitySeeder> logger) : IIdentitySeeder
{
    public const string AdminRoleName = IdentityRoles.Administrator;

    private const string _enabledSettingKey = IdentitySeedOptions.SectionName + ":Enabled";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Identity seeding is disabled ('{SettingKey}' is false); no roles or accounts were created.",
                    _enabledSettingKey);
            }

            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"'{_enabledSettingKey}' is true in the '{environment.EnvironmentName}' " +
                "environment. Seeding a privileged account is only permitted in Development.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await EnsureAdminRoleAsync();
        var user = await EnsureAdminUserAsync(settings);
        await EnsureAdminRoleAssignedAsync(user);

        logger.LogWarning(
            "Seeded the Development administrator account {Email}. Do not enable this outside Development.",
            settings.AdminEmail);
    }

    private async Task EnsureAdminRoleAsync()
    {
        if (await roleManager.RoleExistsAsync(AdminRoleName))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new AppRole(AdminRoleName));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create the '{AdminRoleName}' role: {Describe(result)}");
        }
    }

    private async Task<AppUser> EnsureAdminUserAsync(IdentitySeedOptions settings)
    {
        var existing = await userManager.FindByEmailAsync(settings.AdminEmail);
        if (existing is not null)
        {
            return existing;
        }

        var user = new AppUser
        {
            UserName = settings.AdminEmail,
            Email = settings.AdminEmail,
            EmailConfirmed = true,
            CreatedAt = dateTimeProvider.UtcNow,
        };

        var result = await userManager.CreateAsync(user, settings.AdminPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create the seeded administrator '{settings.AdminEmail}': {Describe(result)}");
        }

        return user;
    }

    private async Task EnsureAdminRoleAssignedAsync(AppUser user)
    {
        if (await userManager.IsInRoleAsync(user, AdminRoleName))
        {
            return;
        }

        var result = await userManager.AddToRoleAsync(user, AdminRoleName);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to add the seeded administrator to '{AdminRoleName}': {Describe(result)}");
        }
    }

    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
}
