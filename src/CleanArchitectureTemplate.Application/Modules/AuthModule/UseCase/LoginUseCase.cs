using System;
using CleanArchitectureTemplate.Application.Commons.Services;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;
using CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;
using FluentValidation;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.UseCase;

public class LoginUseCase : ILoginUseCase
{

    private readonly IUserRepository _userRepository;
    private readonly IAuthService _authService;
    private readonly IValidator<LoginRequestDto> _validator;

    public LoginUseCase(IUserRepository userRepository, IAuthService authService, IValidator<LoginRequestDto> validator)
    {

        _userRepository = userRepository;
        _authService = authService;
        _validator = validator;
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto dto)
    {
        await ValidationService.Validate(this._validator!, dto);
        var user = await this._userRepository.GetUserbyEmailAsync(dto.Email) ?? throw new InvalidOperationException("Email or password is incorrect");

        return await this._authService.AuthenticateUserAsync(user, dto.Password);
    }
}
