using System.Security.Claims;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;

internal interface IAppUserDirectory
{
    Task<AppUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Claim>> GenerateClaimsAsync(AppUser user, CancellationToken cancellationToken = default);
}
