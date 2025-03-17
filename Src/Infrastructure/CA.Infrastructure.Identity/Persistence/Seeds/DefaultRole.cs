using CA.Infrastructure.Identity.Entity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CA.Infrastructure.Identity.Persistence.Seeds;

public static class DefaultRole
{
    public static async Task SeedAsync(RoleManager<AppRole> roleManager)
    {

        if (!await roleManager.Roles.AnyAsync() && !await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new AppRole("Admin"));
    }
}
