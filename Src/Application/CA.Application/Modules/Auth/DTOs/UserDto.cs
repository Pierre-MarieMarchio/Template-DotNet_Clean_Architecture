using System.Security.Claims;

namespace CA.Application.Modules.Auth.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? UserName { get; set; }
    public IEnumerable<Claim>? Claims { get; set; }
}
