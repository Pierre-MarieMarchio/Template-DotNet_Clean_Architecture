using System;
using CleanArchitectureTemplate.Domain.Commons.Bases;

namespace CleanArchitectureTemplate.Domain.Modules.AuthModule;

public class User : BaseModel
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string UserName { get; set; }
}
