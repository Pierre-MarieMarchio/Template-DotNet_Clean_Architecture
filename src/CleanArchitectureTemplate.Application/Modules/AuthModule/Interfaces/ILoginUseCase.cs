using System;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;

public interface ILoginUseCase
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto dto);
}
