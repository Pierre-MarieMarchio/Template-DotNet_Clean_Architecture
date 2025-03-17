namespace CA.Application.Modules.Auth.DTOs.Requests;

public class LoginRequest
{
    public required string Email { get; set; }
    public required string Password { get; set; }
}
