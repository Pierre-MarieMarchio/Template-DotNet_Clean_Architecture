namespace AppTemplate.Application.Features.Auth.Ports;

/// <param name="Token">Single-use. It must never be logged, nor put anywhere a server would see it.</param>
/// <param name="UserName">Carried so the mail can address the holder without a second lookup.</param>
public sealed record PendingPasswordReset(string UserName, string Token);
