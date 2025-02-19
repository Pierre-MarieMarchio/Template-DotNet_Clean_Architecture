using System;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;

public interface IRegisterUseCase
{
    public Task<UserDto> Register(RegisterDto dto);
}
