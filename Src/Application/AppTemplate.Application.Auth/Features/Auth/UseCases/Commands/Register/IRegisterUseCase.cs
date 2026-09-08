using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Register;

/// <summary>
/// Anonymous. Succeeds with an account that cannot yet sign in: proving the address is a separate
/// step, and whether its mail went out travels on the outcome rather than failing the call.
/// </summary>
public interface IRegisterUseCase : IUseCase<RegisterCommand, Result<RegisterOutcome>>;
