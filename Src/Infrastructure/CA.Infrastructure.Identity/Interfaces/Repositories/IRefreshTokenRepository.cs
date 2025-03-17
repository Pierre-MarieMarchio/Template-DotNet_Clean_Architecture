using CA.Infrastructure.Identity.Entity;

namespace CA.Infrastructure.Identity.Interfaces.Repositories;

public interface IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByUserIdAsync(Guid id);
    public Task<RefreshToken?> GetByTokenAsync(string token);
    public Task<RefreshToken> AddAsync(RefreshToken entity);
    public Task<RefreshToken> DeleteAsync(RefreshToken entity);
}
