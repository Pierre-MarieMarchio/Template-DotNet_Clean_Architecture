using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Application.Modules.Auth.DTOs.Response;
using CA.Application.Modules.Auth.Interfaces.Services;
using CA.Application.Modules.Auth.Interfaces.UseCases;

namespace CA.Application.Modules.Auth.UseCases.ConfirmEmail;

class ConfirmEmailUseCase(IAuthService authService) : IConfirmEmailUseCase
{
    public async Task<ConfirmEmailResponse> ExecuteAsync(ConfirmEmailRequest request)
    {
        return await authService.ValidateEmailAsync(request);
    }
}
