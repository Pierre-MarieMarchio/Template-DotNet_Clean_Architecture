using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;

public interface IGetAccountSecurityEventsUseCase
    : IUseCase<GetAccountSecurityEventsQuery, Result<PagedResult<SecurityEventEntry>>>;
