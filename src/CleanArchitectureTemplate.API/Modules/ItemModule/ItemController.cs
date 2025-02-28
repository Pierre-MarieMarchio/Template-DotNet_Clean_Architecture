using System;
using CleanArchitectureTemplate.API.Commons.Bases;
using CleanArchitectureTemplate.Application.Modules.ItemModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.ItemModule.Interfaces;
using CleanArchitectureTemplate.Domain.Modules.ItemModule;


namespace CleanArchitectureTemplate.API.Modules.ItemModule;


public class ItemController : BaseController<Item, ItemDto, ItemCreateDto, ItemUpdateDto>
{
    public ItemController(
        IItemGetUseCase<Item, ItemDto> getUseCase,
        IItemPostUseCase<Item, ItemCreateDto> postUseCase,
        IItemPutUseCase<Item, ItemUpdateDto> putUseCase,
        IItemDeleteUseCase<Item, ItemDto> deleteUseCase
    ) : base(getUseCase, postUseCase, putUseCase, deleteUseCase)
    {
    }
}