using System;
using CleanArchitectureTemplate.Domain.Commons.Bases;

namespace CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;

public interface IBaseRepository<T>
    where T : BaseModel
{
    Task<List<T>> GetAllAsync();
    Task<T?> FindAsync(Guid id);
    Task<T> AddAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task<bool> DeleteAsync(Guid id);
}
