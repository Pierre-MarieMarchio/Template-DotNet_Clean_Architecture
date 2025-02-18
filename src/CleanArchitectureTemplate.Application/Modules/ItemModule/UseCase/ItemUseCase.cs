using System;
using CleanArchitectureTemplate.Application.Commons.Bases;
using CleanArchitectureTemplate.Application.Modules.ItemModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.ItemModule.Interfaces;
using CleanArchitectureTemplate.Domain.Modules.ItemModule;
using CleanArchitectureTemplate.Infrastructure.Modules.ItemModule.Interfaces;
using FluentValidation;

namespace CleanArchitectureTemplate.Application.Modules.ItemModule.UseCase;

public class ItemGetUseCase : BaseGetUseCase<Item, ItemGetDto>, IItemGetUseCase<Item, ItemGetDto>
{

    private readonly IItemService _itemService;

    public ItemGetUseCase(IItemRepository repository, IItemService itemService) : base(repository)
    {
        _itemService = itemService;
    }

    private string GetServiceData()
    {
        var data = this._itemService.GetServiceData() + " is the data !!";
        return data;
    }

    protected override ItemGetDto MapToDTO(Item entity)
    {
        return new ItemGetDto
        {
            Id = entity.Id,
            UserName = entity.UserName,
            ServiceData = GetServiceData(),
        };
    }

}

public class ItemPostUseCase : BasePostUseCase<Item, ItemCreateDto>, IItemPostUseCase<Item, ItemCreateDto>
{

    public ItemPostUseCase(IItemRepository repository, IValidator<ItemCreateDto> validator) : base(repository, validator)
    {
    }


    protected override Item MapToEntity(ItemCreateDto dto)
    {
        return new Item
        {
            UserName = dto.UserName,
        };
    }

    protected override ItemCreateDto MapToDTO(Item entity)
    {
        return new ItemCreateDto
        {
            UserName = entity.UserName,
        };
    }
}


public class ItemPutUseCase : BasePutUseCase<Item, ItemUpdateDto>, IItemPutUseCase<Item, ItemUpdateDto>
{
    public ItemPutUseCase(IItemRepository repository) : base(repository)
    {
    }

    protected override ItemUpdateDto MapToDTO(Item entity)
    {
        return new ItemUpdateDto
        {
            Id = entity.Id,
            UserName = entity.UserName
        };
    }

    protected override void CopyDtoToEntity(ItemUpdateDto dto, Item entity)
    {
        entity.Id = dto.Id;
        entity.UserName = dto.UserName;
    }

}

public class ItemDeleteUseCase : BaseDeleteUseCase<Item, ItemGetDto>, IItemDeleteUseCase<Item, ItemGetDto>
{
    public ItemDeleteUseCase(IItemRepository repository) : base(repository)
    {
    }

}