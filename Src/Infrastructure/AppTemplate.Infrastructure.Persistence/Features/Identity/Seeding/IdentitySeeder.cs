using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;

/// <summary>
/// Creates the <c>Admin</c> role and, in Development only, one administrator account.
///
/// Three things changed from the version this replaces. It is opt-in: nothing happens unless
/// <c>IdentitySeed:Enabled</c> is set. It is refused outside Development, loudly, rather than quietly
/// shipping a known superuser to production. And its password comes from configuration with no
/// default — the previous seeder hard-coded <c>admin</c> / <c>admin</c> with a pre-confirmed
/// <c>admin@admin</c> address and silently ignored <c>!result.Succeeded</c>, so a rejected password
/// looked like a clean start-up.
///
/// It writes through <see cref="UserManager{TUser}"/> rather than through the context, because an
/// account is not just a row: the password has to be hashed with the configured hasher and the
/// security stamp has to be generated. That does mean this type is only constructible once ASP.NET
/// Identity has been composed, which the identity module does; nothing else in this assembly has that
/// dependency.
/// </summary>
internal sealed class IdentitySeeder(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentitySeedOptions> options,
    IHostEnvironment environment,
    IDateTimeProvider dateTimeProvider,
    ILogger<IdentitySeeder> logger) : IIdentitySeeder
{
    public const string AdminRoleName = "Admin";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation(
                "Identity seeding is disabled ('" + IdentitySeedOptions.SectionName +
                ":Enabled' is false); no roles or accounts were created.");
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"'{IdentitySeedOptions.SectionName}:Enabled' is true in the '{environment.EnvironmentName}' " +
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
        // The previous guard was `if (!await roleManager.Roles.AnyAsync() && ...)`, so once any other
        // role existed the Admin role could never be created again.
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
            UserName = settings.AdminUserName,
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

    /// <summary>Includes the <see cref="IdentityResult.Errors"/> so a failure is diagnosable.</summary>
    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
}
