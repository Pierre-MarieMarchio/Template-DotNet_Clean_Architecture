using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;

public sealed class RecordingPasswordHasher : IPasswordHasher<AppUser>
{
    public const string Hash = "a-hash-from-the-configured-hasher";

    public int Verifications { get; private set; }

    public int Hashes { get; private set; }

    public string? LastVerifiedHash { get; private set; }

    public string HashPassword(AppUser user, string password)
    {
        Hashes++;

        return Hash;
    }

    public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
    {
        Verifications++;
        LastVerifiedHash = hashedPassword;

        return PasswordVerificationResult.Failed;
    }
}
