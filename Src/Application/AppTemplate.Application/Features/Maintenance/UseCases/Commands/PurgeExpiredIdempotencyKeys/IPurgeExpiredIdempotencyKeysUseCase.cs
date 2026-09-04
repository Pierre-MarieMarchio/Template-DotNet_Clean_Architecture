using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

public interface IPurgeExpiredIdempotencyKeysUseCase : IUseCase<Result<int>>;
