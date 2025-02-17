using System;
using CleanArchitectureTemplate.Domain.Commons.Models;
using CleanArchitectureTemplate.Infrastructure.Commons.Bases;
using CleanArchitectureTemplate.Infrastructure.Commons.Interfaces;
using CleanArchitectureTemplate.Infrastructure.Persistence.Context;

namespace CleanArchitectureTemplate.Infrastructure.Commons.Repositories;

public class ItemRepository(IdentityDatabaseContext context) : BaseRepository<Item>(context), IItemRepository
{

}
