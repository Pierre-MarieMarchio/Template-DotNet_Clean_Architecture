using System;
using CleanArchitectureTemplate.Application.Commons.Services;
using CleanArchitectureTemplate.Application.Modules.AuthModule.DTOs;
using CleanArchitectureTemplate.Application.Modules.AuthModule.Interfaces;
using CleanArchitectureTemplate.Infrastructure.Modules.AuthModule.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Identity;

namespace CleanArchitectureTemplate.Application.Modules.AuthModule.UseCase;
public class RegisterUseCase : IRegisterUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IValidator<RegisterDto> _validator;

    public RegisterUseCase(IUserRepository userRepository, IValidator<RegisterDto> validator)
    {

        _userRepository = userRepository;
        _validator = validator;
    }

    public async Task<UserDto> Register(RegisterDto dto)
    {
        await ValidationService.Validate(this._validator!, dto);

        var result = await this._userRepository.CreateUserAsync(dto.Email, dto.UserName, dto.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"La création de l'utilisateur a échoué : {errors}");
        }

        var createdUser = await this._userRepository.GetUserbyEmailAsync(dto.Email);

        return new UserDto
        {
            Id = createdUser.Id,
            UserName = createdUser.UserName,
            Email = createdUser.Email,
        };

    }

}
