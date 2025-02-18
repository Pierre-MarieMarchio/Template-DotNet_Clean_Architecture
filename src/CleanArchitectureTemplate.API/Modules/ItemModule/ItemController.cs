using System;
using CleanArchitectureTemplate.API.Commons.Bases;
using CleanArchitectureTemplate.Application.Commons.Interfaces;
using CleanArchitectureTemplate.Application.Modules.ItemModule.DTOs;
using CleanArchitectureTemplate.Domain.Modules.ItemModule;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureTemplate.API.Modules.ItemModule;


[Route("api/Item")]
[ApiController]
public class ItemGetController : BaseGetController<Item, ItemGetDto>
{
    public ItemGetController(IBaseGetUseCase<Item, ItemGetDto> useCase) : base(useCase)
    {
    }
}

[Route("api/Item")]
[ApiController]
public class ItemPostController : BasePostController<Item, ItemCreateDto>
{
    public ItemPostController(IBasePostUseCase<Item, ItemCreateDto> useCase) : base(useCase)
    {
    }

}

[Route("api/Item")]
[ApiController]
public class ItemPutController : BasePutController<Item, ItemUpdateDto>
{
    public ItemPutController(IBasePutUseCase<Item, ItemUpdateDto> useCase) : base(useCase)
    {
    }
}

[Route("api/Item")]
[ApiController]
public class ItemDeleteController : BaseDeleteController<Item, ItemGetDto>
{
    public ItemDeleteController(IBaseDeleteUseCase<Item, ItemGetDto> useCase) : base(useCase)
    {
    }
}