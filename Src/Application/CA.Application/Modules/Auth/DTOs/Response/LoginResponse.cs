using CA.Application.Modules.Auth.DTOs;
using System.Text.Json.Serialization;

namespace CA.Application.Modules.Auth.DTOs.Response;

public class LoginResponse
{
    public required Guid UserId { get; set; }
    public required string UserName { get; set; }
    public required string Email { get; set; }
    public required string AccessToken { get; set; }
    public required double ExpirationTime { get; set; }

    [JsonIgnore]
    public RefreshTokenDto? RefreshToken { get; set; }
}
