using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;

/// <summary>
/// Stands in for the configured <see cref="IPasswordHasher{TUser}"/> and counts the key derivations
/// asked of it, which is the only way to assert that two code paths cost the same.
/// </summary>
public sealed class RecordingPasswordHasher : IPasswordHasher<AppUser>
{
    /// <summary>What <see cref="HashPassword"/> returns, so a caller can assert which hash it verified.</summary>
    public const string Hash = "a-hash-from-the-configured-hasher";

    public int Verifications { get; private set; }

    public string? LastVerifiedHash { get; private set; }

    public string HashPassword(AppUser user, string password) => Hash;

    public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
    {
        Verifications++;
        LastVerifiedHash = hashedPassword;

        return PasswordVerificationResult.Failed;
    }
}
