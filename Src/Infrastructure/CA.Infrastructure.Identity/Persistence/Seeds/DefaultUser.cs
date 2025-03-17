using CA.Infrastructure.Identity.Entity;
using Microsoft.AspNetCore.Identity;

namespace CA.Infrastructure.Identity.Persistence.Seeds;

public static class DefaultUser
{
    public static async Task SeedAsync(UserManager<AppUser> userManager)
    {
        var defaultUser = new AppUser
        {
            UserName = "Admin",
            Email = "Admin@Admin.com",
            EmailConfirmed = true
        };

        var user = await userManager.FindByEmailAsync(defaultUser.Email);

        if (user == null)
        {
            var result = await userManager.CreateAsync(defaultUser, "admin");

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(defaultUser, "Admin");
            }
        }
        else
        {
            if (!await userManager.IsInRoleAsync(user, "Admin"))
            {
                await userManager.AddToRoleAsync(user, "Admin");
            }
        }
    }

}
