using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.PurgeExpiredRefreshTokens;

/// <summary>
/// Administrative housekeeping, driven by a schedule rather than by any caller's own request.
/// </summary>
/// <returns>How many grants were deleted, so a scheduled run has something to report.</returns>
public interface IPurgeExpiredRefreshTokensUseCase : IUseCase<Result<int>>;
