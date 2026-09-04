namespace AppTemplate.Application.Features.Auth.Ports;

/// <summary>The identifying facts about an account, as everything outside the user store sees it.</summary>
public sealed record AccountIdentity(Guid UserId, string UserName, string Email);
