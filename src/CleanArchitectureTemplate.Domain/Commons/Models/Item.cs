using System;

namespace CleanArchitectureTemplate.Domain.Commons.Models;

public class Item
{
    public Guid Id { get; set; }
    public required string UserName { get; set; }
}
