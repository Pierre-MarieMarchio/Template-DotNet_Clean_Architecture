using CA.Infrastructure.Identity.Entity;
using CA.Infrastructure.Identity.Interfaces.Repositories;
using CA.Infrastructure.Identity.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace CA.Infrastructure.Identity.Repositories;

public class RefreshTokenRepository(AppIdentityDbContext dbContext) : IRefreshTokenRepository
{

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        return await dbContext.Set<RefreshToken>().Include(rt => rt.User).FirstOrDefaultAsync(t => t.Token == token);
    }

    public async Task<RefreshToken?> GetByUserIdAsync(Guid id)
    {
        return await dbContext.Set<RefreshToken>().Include(rt => rt.User).FirstOrDefaultAsync(rt => rt.UserId == id);
    }

    public async Task<RefreshToken> AddAsync(RefreshToken entity)
    {
        await dbContext.Set<RefreshToken>().AddAsync(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

    public async Task<RefreshToken> DeleteAsync(RefreshToken entity)
    {
        dbContext.Set<RefreshToken>().Remove(entity);
        await dbContext.SaveChangesAsync();
        return entity;
    }

}
