namespace AppTemplate.Application.Auth.Features.Auth.Ports.EmailTokens;

/// <summary>A token minted for an account's mail, with the name the message greets.</summary>
public sealed record IssuedEmailToken(string UserName, string Token);
