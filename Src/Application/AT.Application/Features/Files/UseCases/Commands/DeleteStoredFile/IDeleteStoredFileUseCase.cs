using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

public interface IDeleteStoredFileUseCase : IUseCase<DeleteStoredFileCommand, Result>;
