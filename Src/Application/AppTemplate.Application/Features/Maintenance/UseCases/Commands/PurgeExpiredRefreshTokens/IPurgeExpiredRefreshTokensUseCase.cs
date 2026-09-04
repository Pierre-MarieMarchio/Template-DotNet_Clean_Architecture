using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;

public interface IPurgeExpiredRefreshTokensUseCase : IUseCase<Result<int>>;
