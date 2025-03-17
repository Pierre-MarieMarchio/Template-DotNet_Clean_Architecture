using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Application.Modules.Auth.DTOs.Response;
using CA.Application.Modules.Auth.Interfaces.Services;
using CA.Application.Modules.Auth.Interfaces.UseCases;
using FluentValidation;

namespace CA.Application.Modules.Auth.UseCases.Login;

public class LoginUseCase(IAuthService authService, IValidator<LoginRequest> validator) : ILoginUseCase
{
    public async Task<LoginResponse> ExecuteAsync(LoginRequest request)
    {
        await validator.ValidateAsync(request);
        return await authService.LoginAsync(request);
    }
}
