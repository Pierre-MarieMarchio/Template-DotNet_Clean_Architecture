using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

public interface IRegisterFileUseCase : IUseCase<RegisterFileCommand, Result<RegisterFileOutcome>>;
