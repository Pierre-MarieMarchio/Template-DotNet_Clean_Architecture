using System;
using System.Data.Common;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;

public class UserDto
{

    public required Guid Id { get; set; }
    public required string Email { get; set; }
    public required string UserName { get; set; }
}
