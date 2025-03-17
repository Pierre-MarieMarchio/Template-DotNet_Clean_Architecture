namespace CA.Application.Modules.Auth.DTOs.Response;

public class AuthenticateResponse
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }

}
