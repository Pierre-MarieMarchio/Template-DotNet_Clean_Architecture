namespace CA.Application.Modules.Auth.DTOs.Response;

public class RegisterResponse
{
    public required Guid Id { get; set; }
    public required string Email { get; set; }
    public required string UserName { get; set; }

}
