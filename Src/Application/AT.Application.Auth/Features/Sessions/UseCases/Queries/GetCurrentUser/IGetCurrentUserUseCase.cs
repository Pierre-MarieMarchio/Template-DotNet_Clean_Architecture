using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.GetCurrentUser;

public interface IGetCurrentUserUseCase : IUseCase<Result<GetCurrentUserOutcome>>;
