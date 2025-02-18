using System;
using CleanArchitectureTemplate.Domain.Commons.Bases;

namespace CleanArchitectureTemplate.Domain.Modules.ItemModule;

public class Item : BaseModel
{
    public Guid Id { get; set; }
    public required string UserName { get; set; }
}
