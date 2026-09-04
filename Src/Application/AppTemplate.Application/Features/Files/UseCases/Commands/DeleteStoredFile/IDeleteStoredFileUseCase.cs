using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

public interface IDeleteStoredFileUseCase : IUseCase<DeleteStoredFileCommand, Result>;
