namespace CA.Application.Modules.Auth.DTOs;

public class RefreshTokenDto
{
    public Guid Id { get; set; }
    public required string Token { get; set; }
    public required Guid UserId { get; set; }
    public DateTime ExpirationDate { get; set; }

}
