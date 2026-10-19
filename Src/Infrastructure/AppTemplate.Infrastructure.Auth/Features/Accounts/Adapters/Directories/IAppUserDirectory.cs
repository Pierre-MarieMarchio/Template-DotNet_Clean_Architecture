using System.Security.Claims;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;

/// <summary>Looking an account up and reading its current claims — what the token adapters in this
/// module need and no application port exposes. Internal: only the composition root needs to know
/// which class satisfies it, and a caller outside the module able to ask for an account's claims
/// could grant itself any of them. <b>Cancellation is observed on entry only:</b> neither
/// <see cref="UserManager{TUser}"/> nor <see cref="SignInManager{TUser}"/> accepts a
/// <see cref="CancellationToken"/> here; the parameter is kept for the entry check, not because the
/// work is interruptible.</summary>
internal interface IAppUserDirectory
{
    Task<AppUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The claims the account carries now, including the security stamp, so a claim revoked between
    /// two issuances is absent from the next token.
    /// </summary>
    Task<IReadOnlyCollection<Claim>> GenerateClaimsAsync(AppUser user, CancellationToken cancellationToken = default);
}
