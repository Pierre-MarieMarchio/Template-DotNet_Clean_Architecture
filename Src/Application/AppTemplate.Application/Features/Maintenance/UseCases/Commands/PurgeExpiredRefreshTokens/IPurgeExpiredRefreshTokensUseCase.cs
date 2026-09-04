using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;

public interface IPurgeExpiredRefreshTokensUseCase : IUseCase<Result<int>>;
