using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Application.Modules.Auth.DTOs.Response;

namespace CA.Application.Modules.Auth.Interfaces.Services;

public interface IAuthService
{
    public Task<RegisterResponse> RegisterAccountAsync(RegisterRequest registerRequest, string password);
    public Task<LoginResponse> LoginAsync(LoginRequest loginRequest);
    public Task<AuthenticateResponse> AuthenticateAsync(string requestRefreshToken);
    public Task<ConfirmEmailResponse> ValidateEmailAsync(ConfirmEmailRequest request);
}
