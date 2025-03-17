namespace CA.Application.Modules.Auth.DTOs.Requests;

public class RegisterRequest
{
    public required string UserName { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
}
