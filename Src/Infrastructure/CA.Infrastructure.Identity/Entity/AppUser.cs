using Microsoft.AspNetCore.Identity;

namespace CA.Infrastructure.Identity.Entity;

public class AppUser : IdentityUser<Guid>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;

    public virtual ICollection<RefreshToken>? RefreshTokens { get; set; }
}
