using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// The owner comes from <see cref="ICurrentUser"/> and is deliberately not part of the command, so
/// no caller can register a file into somebody else's allowance.
/// </summary>
public interface IRegisterFileUseCase : IUseCase<RegisterFileCommand, Result<RegisterFileOutcome>>;
