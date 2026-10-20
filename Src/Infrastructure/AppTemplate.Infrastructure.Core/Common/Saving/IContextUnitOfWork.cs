using AppTemplate.Application.Core.Common.Ports;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving;

public interface IContextUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext;
