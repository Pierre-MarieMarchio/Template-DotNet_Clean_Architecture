using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// The owner comes from <see cref="ICurrentUser"/>, not the command, so nobody can register into
/// another owner's allowance.
/// </summary>
public interface IRegisterFileUseCase : IUseCase<RegisterFileCommand, Result<RegisterFileOutcome>>;
