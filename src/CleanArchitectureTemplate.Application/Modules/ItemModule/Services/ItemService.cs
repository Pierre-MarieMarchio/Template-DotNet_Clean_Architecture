using System;
using CleanArchitectureTemplate.Application.Modules.ItemModule.Interfaces;

namespace CleanArchitectureTemplate.Application.Modules.ItemModule.Services;

public class ItemService : IItemService
{

    public string GetServiceData()
    {
        return "the" + "datas";
    }

}
