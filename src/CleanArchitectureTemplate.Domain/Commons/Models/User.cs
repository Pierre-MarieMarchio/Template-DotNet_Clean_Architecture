using System;

namespace CleanArchitectureTemplate.Domain.Models;

public class User
{
    public Guid Id { get; set; }
    public required string UserName { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
}
