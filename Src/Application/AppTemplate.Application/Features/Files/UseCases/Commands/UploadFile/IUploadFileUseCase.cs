using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Files.Dtos;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;

/// <summary>
/// Registers a file and writes its bytes in one call. The owner comes from <see cref="ICurrentUser"/>.
/// </summary>
public interface IUploadFileUseCase : IUseCase<UploadFileCommand, Result<Versioned<StoredFileDto>>>;
