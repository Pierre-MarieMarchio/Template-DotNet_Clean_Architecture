namespace CA.Application.Modules.Auth.DTOs.Requests;

public class ConfirmEmailRequest
{
    public required string Email { get; set; }
    public required string Token { get; set; }
}
