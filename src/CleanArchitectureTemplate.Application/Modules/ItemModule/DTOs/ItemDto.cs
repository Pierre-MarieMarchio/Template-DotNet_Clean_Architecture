using System;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Application.Commons.Interfaces;

namespace CleanArchitectureTemplate.Application.Modules.ItemModule.DTOs;


public class ItemDto : BaseDto, IEntityWithId<Guid>
{
    public Guid Id { get; set; }
    public required string UserName { get; set; }
    public required string ServiceData { get; set; }
}


public class ItemUpdateDto : BaseDto, IEntityWithId<Guid>
{
    public required Guid Id { get; set; }
    public required string UserName { get; set; }
}



public class ItemCreateDto : BaseDto
{
    public required string UserName { get; set; }
}