using System.Security.Claims;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Common.Directories;

/// <summary>
/// Looking an account up and reading its current claims — the two things the token adapters in this
/// module need and that no application port exposes, because a stored account and a claim set are
/// this module's own vocabulary.
/// <para>
/// Internal: only the composition root needs to know which class satisfies it, and a caller outside
/// the module able to ask for an account's claims could grant itself any of them.
/// </para>
/// <para>
/// <b>Cancellation is observed on entry only, and cannot be propagated.</b> Neither
/// <see cref="UserManager{TUser}"/> nor <see cref="SignInManager{TUser}"/> accepts a
/// <see cref="CancellationToken"/> on the operations used here, so a token cancelled while one of
/// them is in flight does not stop it. The parameter is kept because these calls sit on a request's
/// path and the check at entry is worth having, not because the work is interruptible.
/// </para>
/// </summary>
internal interface IAppUserDirectory
{
    Task<AppUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The claims the account carries now, including the security stamp, so a claim revoked between
    /// two issuances is absent from the next token.
    /// </summary>
    Task<IReadOnlyCollection<Claim>> GenerateClaimsAsync(AppUser user, CancellationToken cancellationToken = default);
}
