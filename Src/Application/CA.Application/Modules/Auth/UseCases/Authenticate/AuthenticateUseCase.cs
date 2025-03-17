using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Application.Modules.Auth.DTOs.Response;
using CA.Application.Modules.Auth.Interfaces.Services;
using CA.Application.Modules.Auth.Interfaces.UseCases;
using FluentValidation;

namespace CA.Application.Modules.Auth.UseCases.Authenticate;


public class AuthenticateUseCase(IAuthService authService, IValidator<AuthenticateRequest> validator) : IAuthenticateUseCase
{
    public async Task<AuthenticateResponse> ExecuteAsync(AuthenticateRequest request)
    {
        await validator.ValidateAsync(request);
        return await authService.AuthenticateAsync(request.RefreshToken);
    }
}
