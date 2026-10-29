using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Queries.ListAccessKeys;

public interface IListAccessKeysUseCase : IUseCase<Result<IReadOnlyList<LiveAccessKey>>>;
