using System;

namespace CleanArchitectureTemplate.Application.Commons.Interfaces;

public interface IEntityWithId<T>
{
    T Id { get; set; }
}
