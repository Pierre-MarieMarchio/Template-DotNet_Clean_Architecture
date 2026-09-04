namespace AppTemplate.Api.Common.Security;

/// <summary>
/// How much one partition may spend, and over what window.
/// </summary>
/// <remarks>
/// The two numbers are what a rate limit <em>is</em>, and they are the whole of what
/// <see cref="IRateLimitCounters"/> is told: everything else — which partition a request falls into,
/// what a refusal looks like on the wire — is decided on either side of the counters and stays there,
/// so that an implementation swapping the counters cannot accidentally take those decisions with it.
/// </remarks>
internal sealed record RateLimitBudget(int PermitLimit, TimeSpan Window);
