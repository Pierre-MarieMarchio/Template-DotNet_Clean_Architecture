using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

public interface IPurgeExpiredIdempotencyKeysUseCase : IUseCase<Result<int>>;
