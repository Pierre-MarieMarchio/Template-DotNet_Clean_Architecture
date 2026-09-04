using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser;

/// <summary>Whole input is ambient: the caller's own id, taken from the request's principal.</summary>
public interface IGetCurrentUserUseCase : IUseCase<Result<CurrentUserResponse>>;
