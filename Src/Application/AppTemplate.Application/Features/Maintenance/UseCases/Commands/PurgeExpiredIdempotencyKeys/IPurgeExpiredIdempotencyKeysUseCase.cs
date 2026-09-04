using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

public interface IPurgeExpiredIdempotencyKeysUseCase : IUseCase<Result<int>>;
