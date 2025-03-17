namespace CA.Infrastructure.Identity.Entity;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Token { get; set; }
    public required Guid UserId { get; set; }
    public required DateTime ExpirationDate { get; set; }

    public required AppUser User { get; set; }
}
