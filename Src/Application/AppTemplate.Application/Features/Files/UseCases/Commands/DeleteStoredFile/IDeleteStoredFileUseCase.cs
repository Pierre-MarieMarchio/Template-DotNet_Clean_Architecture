using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

public interface IDeleteStoredFileUseCase : IUseCase<DeleteStoredFileCommand, Result>;
