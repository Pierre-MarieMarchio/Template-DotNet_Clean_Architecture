using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.LogoutEverywhere;

/// <summary>Whole input is ambient: the caller's own id, taken from the request's principal.</summary>
public interface ILogoutEverywhereUseCase : IUseCase<Result>;
