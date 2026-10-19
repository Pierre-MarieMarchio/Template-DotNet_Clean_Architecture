namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetMySecurityEvents;

/// <summary>
/// Which page of the caller's own events to read. No account id: whose events these are comes from
/// the caller's identity, so there is nothing here to point at somebody else.
/// </summary>
public sealed record GetMySecurityEventsQuery(int? Page, int? PageSize);
