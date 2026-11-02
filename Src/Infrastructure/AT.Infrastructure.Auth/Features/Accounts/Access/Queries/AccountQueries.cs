using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Access.Queries;

internal sealed class AccountQueries(
    AuthDbContext context,
    ILookupNormalizer normalizer,
    IDateTimeProvider dateTimeProvider) : IAccountQueries
{
    public async Task<PagedResult<AccountSummary>> ListAsync(
        string? emailPrefix,
        PageRequest paging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paging);

        var users = context.Users.AsNoTracking();

        if (emailPrefix is not null)
        {
            string normalizedPrefix = normalizer.NormalizeEmail(emailPrefix);
            users = users.Where(user => user.NormalizedEmail!.StartsWith(normalizedPrefix));
        }
        else
        {
            users = users.Where(user => user.NormalizedEmail != null);
        }

        int totalCount = await users.CountAsync(cancellationToken);
        int page = paging.Page ?? 1;
        var now = dateTimeProvider.UtcNow;

        var rows = await users
            .OrderBy(user => user.NormalizedEmail)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .Select(user => new
            {
                user.Id,
                Email = user.Email!,
                user.EmailConfirmed,
                LockedOut = user.LockoutEnd != null && user.LockoutEnd > now,
                user.TwoFactorEnabled,
                user.CreatedAt,
                Roles = context.UserRoles
                    .Where(link => link.UserId == user.Id)
                    .Join(context.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Name!)
                    .OrderBy(name => name)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AccountSummary(
                row.Id,
                row.Email,
                row.EmailConfirmed,
                row.LockedOut,
                row.TwoFactorEnabled,
                row.Roles,
                row.CreatedAt))
            .ToList();

        return PagedResult.Offset(items, page, paging.PageSize, totalCount);
    }
}
