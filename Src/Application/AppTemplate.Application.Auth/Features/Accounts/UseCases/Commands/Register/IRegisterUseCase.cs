using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.Register;

/// <summary>
/// Anonymous. Succeeds the same way whatever the address's state, and tells the address's holder by
/// mail what happened.
/// </summary>
public interface IRegisterUseCase : IUseCase<RegisterCommand, Result>;
