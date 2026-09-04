using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;

public interface IPurgeExpiredRefreshTokensUseCase : IUseCase<Result<int>>;
