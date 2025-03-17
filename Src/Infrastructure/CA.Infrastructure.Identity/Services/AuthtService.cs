using CA.Application.Modules.Auth.DTOs.Requests;
using CA.Application.Modules.Auth.DTOs.Response;
using CA.Application.Modules.Auth.Interfaces.Services;
using CA.Infrastructure.Identity.Interfaces.Services;
using CA.Infrastructure.Identity.Settings;
using System.Net;

namespace CA.Infrastructure.Identity.Services;

public class AuthtService(
    IAppUserService appUserService,
    ITokenAccessService tokenAccessService,
    ITokenRefreshService tokenRefreshService,
    IRegisterMailerService registerMailerService,
    JwtSettings jwtSettings) : IAuthService
{
    public async Task<RegisterResponse> RegisterAccountAsync(RegisterRequest registerRequest, string password)
    {
        var user = appUserService.MapToAppUser(registerRequest);
        var result = await appUserService.CreateAsync(user, registerRequest.Password);
        var token = await appUserService.GenerateEmailTokenAsync(result);
        var encodedToken = WebUtility.UrlEncode(token);

        await registerMailerService.SendConfirmationEmailAsync(user.UserName!, user.Email!, encodedToken);
        return new RegisterResponse
        {
            Id = result.Id,
            UserName = result.UserName ?? string.Empty,
            Email = result.Email ?? string.Empty,
        };
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest loginRequest)
    {
        var user = await appUserService.CheckAndGeAsync(loginRequest);
        var userClaims = await appUserService.GenarateClaimsAsync(user);

        await tokenRefreshService.DeleteRefreshTokenAsync(user);

        var accessToken = tokenAccessService.GenerateAccessToken(userClaims);
        var refreshToken = await tokenRefreshService.GenerateRefreshTokenAsync(user, userClaims);

        return new LoginResponse
        {
            UserId = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            AccessToken = accessToken,
            ExpirationTime = jwtSettings.DurationInMinutes,
            RefreshToken = tokenRefreshService.MapToRefreshTokenDto(refreshToken)
        };
    }

    public async Task<AuthenticateResponse> AuthenticateAsync(string requestRefreshToken)
    {
        if (!tokenRefreshService.VerifyRefreshToken(requestRefreshToken))
        {
            return new AuthenticateResponse { Success = false };
        }

        var refreshToken = await tokenRefreshService.GetRefreshTokenByTokenAsync(requestRefreshToken);
        if (refreshToken == null)
        {
            return new AuthenticateResponse { Success = false };
        }

        var userClaims = await appUserService.GenarateClaimsAsync(refreshToken.User);
        var newAccessToken = tokenAccessService.GenerateAccessToken(userClaims);

        return new AuthenticateResponse
        {
            Success = true,
            AccessToken = newAccessToken,
        };
    }

    public async Task<ConfirmEmailResponse> ValidateEmailAsync(ConfirmEmailRequest request)
    {

        var result = await appUserService.ValidateEmailAsync(request.Email, request.Token);

        return new ConfirmEmailResponse
        {
            Success = result.Succeeded,
            Message = string.Join(", ", result.Errors.Select(e => e.Description)),
        };
    }
}
