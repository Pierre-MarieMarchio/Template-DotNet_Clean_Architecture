using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;

/// <summary>
/// Administrative housekeeping, driven by a schedule rather than by any caller's own request.
/// </summary>
/// <returns>How many events were deleted, so a scheduled run has something to report.</returns>
public interface IPurgeExpiredSecurityEventsUseCase : IUseCase<Result<int>>;
