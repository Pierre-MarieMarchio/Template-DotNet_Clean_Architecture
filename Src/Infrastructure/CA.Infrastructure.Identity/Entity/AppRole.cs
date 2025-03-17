using Microsoft.AspNetCore.Identity;

namespace CA.Infrastructure.Identity.Entity;

public class AppRole(string name) : IdentityRole<Guid>(name)
{
}
