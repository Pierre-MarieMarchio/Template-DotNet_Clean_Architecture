using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;

public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
    }

    public AppRole(string roleName) : base(roleName)
    {
    }
}
