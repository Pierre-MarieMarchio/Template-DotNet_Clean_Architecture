using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

/// <summary>
/// The port a maintenance endpoint or a scheduled loop resolves. It answers how many keys it removed,
/// which is the only thing an operator can act on.
/// </summary>
public interface IPurgeExpiredIdempotencyKeysUseCase : IUseCase<Result<int>>;
