using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;

/// <summary>
/// The account and login tables, in memory, behind the four ASP.NET Identity store interfaces a
/// <see cref="UserManager{TUser}"/> needs to create an account and attach a provider identity to it.
/// <para>
/// Written out rather than substituted, because what is under test runs <em>through</em>
/// <c>UserManager</c> and its <see cref="UserValidator{TUser}"/>: the allowed-character check and
/// the duplicate-name check are the framework's, and a substitute configured to return
/// <c>IdentityResult.Success</c> would be a test asserting that a mock was told to succeed. The
/// uniqueness rules here are the ones the real schema enforces with a unique index.
/// </para>
/// </summary>
internal sealed class AccountRows :
    IUserStore<AppUser>,
    IUserEmailStore<AppUser>,
    IUserLoginStore<AppUser>,
    IUserSecurityStampStore<AppUser>
{
    private readonly List<AppUser> _users = [];
    private readonly List<(Guid UserId, UserLoginInfo Login)> _logins = [];

    internal IReadOnlyList<AppUser> Users => _users;

    internal AppUser Add(string userName, string email, bool emailConfirmed)
    {
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = emailConfirmed,

            // Every real row has one, and UserManager.AddLoginAsync rotates it — attaching a
            // credential invalidates the access tokens issued before it — so a row without one
            // fails there rather than in anything under test.
            SecurityStamp = Guid.CreateVersion7().ToString("N"),
        };

        _users.Add(user);

        return user;
    }

    internal void Link(Guid userId, string provider, string subject) =>
        _logins.Add((userId, new UserLoginInfo(provider, subject, provider)));

    internal bool IsLinked(Guid userId, string provider, string subject) =>
        _logins.Any(entry => entry.UserId == userId
            && entry.Login.LoginProvider == provider
            && entry.Login.ProviderKey == subject);

    public Task<IdentityResult> CreateAsync(AppUser user, CancellationToken cancellationToken)
    {
        // The unique indexes the real schema carries. Without them the duplicate paths under test
        // would never be reached.
        if (_users.Any(existing => existing.NormalizedUserName == user.NormalizedUserName))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "DuplicateUserName" }));
        }

        if (_users.Any(existing => existing.NormalizedEmail == user.NormalizedEmail))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail" }));
        }

        _users.Add(user);

        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken cancellationToken)
    {
        _users.Remove(user);
        _logins.RemoveAll(entry => entry.UserId == user.Id);

        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(IdentityResult.Success);

    public Task<AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Find(user => user.Id.ToString() == userId));

    public Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Find(user => user.NormalizedUserName == normalizedUserName));

    public Task<AppUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Find(user => user.NormalizedEmail == normalizedEmail));

    public Task<AppUser?> FindByLoginAsync(
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var match = _logins.Find(entry =>
            entry.Login.LoginProvider == loginProvider && entry.Login.ProviderKey == providerKey);

        return Task.FromResult(match.Login is null ? null : _users.Find(user => user.Id == match.UserId));
    }

    public Task AddLoginAsync(AppUser user, UserLoginInfo login, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);

        // No duplicate check here on purpose: UserManager.AddLoginAsync does it itself, by looking
        // the pair up first and answering LoginAlreadyAssociated. Repeating it here would make the
        // test pass through a rule the production path never reaches.
        _logins.Add((user.Id, login));

        return Task.CompletedTask;
    }

    public Task RemoveLoginAsync(
        AppUser user,
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        _logins.RemoveAll(entry => entry.UserId == user.Id
            && entry.Login.LoginProvider == loginProvider
            && entry.Login.ProviderKey == providerKey);

        return Task.CompletedTask;
    }

    public Task<IList<UserLoginInfo>> GetLoginsAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult<IList<UserLoginInfo>>(
            [.. _logins.Where(entry => entry.UserId == user.Id).Select(entry => entry.Login)]);

    public Task<string> GetUserIdAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(AppUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;

        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(AppUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;

        return Task.CompletedTask;
    }

    public Task SetEmailAsync(AppUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;

        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(AppUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;

        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedEmailAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(AppUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;

        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(AppUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;

        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(AppUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    public void Dispose()
    {
        // Nothing to release: the rows are a list.
    }
}
