using System;
using CleanArchitectureTemplate.Domain.Modules.ItemModule;
using CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;

namespace CleanArchitectureTemplate.Infrastructure.Modules.ItemModule.Interfaces;

public interface IItemRepository : IBaseRepository<Item>
{

}
