using System;

namespace CleanArchitectureTemplate.Infrastructure.Commons.Bases;

public interface IBaseRepository<T>
    where T : class
{
    Task<List<T>> GetAllAsync();
    Task<T?> FindAsync(Guid id);
    Task<T> AddAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task<bool> DeleteAsync(Guid id);
}
