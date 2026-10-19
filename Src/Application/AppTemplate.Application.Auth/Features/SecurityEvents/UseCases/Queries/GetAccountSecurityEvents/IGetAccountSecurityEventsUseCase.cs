using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;

/// <summary>One named account's security events, newest first, for an administrator.</summary>
public interface IGetAccountSecurityEventsUseCase
    : IUseCase<GetAccountSecurityEventsQuery, Result<PagedResult<SecurityEventEntry>>>;
