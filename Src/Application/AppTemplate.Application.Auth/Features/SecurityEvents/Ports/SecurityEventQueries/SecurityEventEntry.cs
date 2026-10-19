using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;

/// <summary>
/// One recorded event as it reads back. <see cref="IpAddress"/> and <see cref="UserAgent"/> are
/// what the request that produced it carried, and <c>null</c> when it carried none or the event had
/// no request behind it. <see cref="Detail"/> is the event's one small datum: the refusal status of
/// a failed sign-in, or the role a grant or revocation named.
/// </summary>
public sealed record SecurityEventEntry(
    Guid Id,
    SecurityEventKind Kind,
    DateTimeOffset OccurredAt,
    string? IpAddress,
    string? UserAgent,
    string? Detail);
