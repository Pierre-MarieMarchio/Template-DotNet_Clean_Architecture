namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// When the access token authenticating the current request was issued. One use: the re-authentication
/// rule for an account with neither a password nor an armed second factor — a provider-linked account,
/// typically — where the only proof the session is the holder's own is that it began moments ago.
/// <para>
/// Read by the host from the token it validated, like <c>ICurrentUser</c> is: the application layer
/// asks how fresh the proof is, never which scheme produced it.
/// </para>
/// </summary>
public interface ICurrentAccessToken
{
    /// <summary>
    /// The token's issued-at instant, in UTC; <c>null</c> when the request's credential carries no
    /// issued-at claim a decision can read. A missing answer is a refusal, never a pass.
    /// </summary>
    DateTimeOffset? IssuedAt { get; }
}
