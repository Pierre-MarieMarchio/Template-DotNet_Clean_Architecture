using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.GetCurrentUser;

/// <summary>Whole input is ambient: the caller's own id, taken from the request's principal.</summary>
public interface IGetCurrentUserUseCase : IUseCase<Result<GetCurrentUserOutcome>>;
