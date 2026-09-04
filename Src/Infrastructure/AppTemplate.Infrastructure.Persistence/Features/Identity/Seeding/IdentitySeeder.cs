using AppTemplate.Application.Common.Ports;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;

/// <summary>
/// Creates the <c>Admin</c> role and, in Development only, one administrator account.
/// <para>
/// Three guarantees, in order. It is opt-in: nothing happens unless <c>IdentitySeed:Enabled</c> is
/// set. It is refused outside Development, loudly, rather than shipping a known superuser to
/// production. And a failed create throws instead of being ignored, so a password the hasher
/// rejected cannot pass for a clean start-up.
/// </para>
/// <para>
/// It writes through <see cref="UserManager{TUser}"/> rather than through the context, because an
/// account is not just a row: the password has to be hashed with the configured hasher and the
/// security stamp has to be generated. That does mean this type is only constructible once ASP.NET
/// Identity has been composed, which the identity module does; nothing else in this assembly has that
/// dependency.
/// </para>
/// </summary>
internal sealed class IdentitySeeder(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentitySeedOptions> options,
    IHostEnvironment environment,
    IDateTimeProvider dateTimeProvider,
    ILogger<IdentitySeeder> logger) : IIdentitySeeder
{
    /// <summary>
    /// Read from <see cref="IdentityRoles"/> rather than spelled again here, so the role this
    /// creates and the role the API's administrator policy requires cannot drift apart.
    /// </summary>
    public const string AdminRoleName = IdentityRoles.Administrator;

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
        // Asks whether this role exists, not whether any role does: a guard on the emptiness of the
        // whole table would stop creating Admin as soon as some other role was seeded.
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
