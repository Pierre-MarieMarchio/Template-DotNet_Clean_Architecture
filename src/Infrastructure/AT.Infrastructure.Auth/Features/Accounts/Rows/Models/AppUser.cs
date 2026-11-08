using AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;

public sealed class AppUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }

    internal ICollection<RefreshToken> RefreshTokens { get; } = [];

    internal ICollection<RefreshTokenFamilyRecord> RefreshTokenFamilies { get; } = [];
}
