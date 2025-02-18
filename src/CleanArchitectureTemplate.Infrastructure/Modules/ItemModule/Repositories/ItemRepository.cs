using System;
using CleanArchitectureTemplate.Domain.Modules.ItemModule;
using CleanArchitectureTemplate.Infrastructure.Commons.Bases;
using CleanArchitectureTemplate.Infrastructure.Modules.ItemModule.Interfaces;
using CleanArchitectureTemplate.Infrastructure.Persistence.Context;

namespace CleanArchitectureTemplate.Infrastructure.Modules.ItemModule.Repositories;

public class ItemRepository(IdentityDatabaseContext context) : BaseRepository<Item>(context), IItemRepository
{

}
