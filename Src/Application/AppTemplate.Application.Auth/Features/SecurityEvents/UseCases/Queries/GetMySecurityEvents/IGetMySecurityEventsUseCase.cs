using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetMySecurityEvents;

/// <summary>The signed-in caller's own security events, newest first.</summary>
public interface IGetMySecurityEventsUseCase
    : IUseCase<GetMySecurityEventsQuery, Result<PagedResult<SecurityEventEntry>>>;
