using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Application.Modules.Auth.DTOs.Response;
using CA.Application.Modules.Auth.Interfaces.Services;
using CA.Application.Modules.Auth.Interfaces.UseCases;
using FluentValidation;

namespace CA.Application.Modules.Auth.UseCases.AccountRegister;

public class RegisterUseCase(IAuthService authService, IValidator<RegisterRequest> validator) : IRegisterUseCase
{
    public async Task<RegisterResponse> ExecuteAsync(RegisterRequest request)
    {
        await validator.ValidateAsync(request);
        return await authService.RegisterAccountAsync(request, request.Password);
    }
}
