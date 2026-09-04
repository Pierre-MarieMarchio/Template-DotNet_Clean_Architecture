using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.UserProfiles;

/// <summary>
/// Reads an account's current profile from the store.
/// <para>
/// Never built from the bearer token's claims: a claim is only as fresh as the access token that
/// carries it, so a profile assembled that way would answer with whatever was true up to
/// <c>AccessTokenLifetimeInMinutes</c> ago. A role granted or revoked in between would stay invisible
/// to the caller until the token itself expired.
/// </para>
/// <para>
/// <b>Cancellation is observed on entry only</b>, for the reason given on <see cref="IUserAccountsService"/>.
/// </para>
/// </summary>
public interface IUserProfilesService
{
    /// <returns><c>null</c> when the account no longer exists.</returns>
    Task<UserProfile?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
