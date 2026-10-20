namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;

public sealed record GetAccountSecurityEventsQuery(Guid UserId, int? Page, int? PageSize);
