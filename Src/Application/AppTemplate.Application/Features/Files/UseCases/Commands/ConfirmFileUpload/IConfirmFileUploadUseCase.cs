using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.Files.Dtos;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

public interface IConfirmFileUploadUseCase
    : IUseCase<ConfirmFileUploadCommand, Result<Versioned<StoredFileDto>>>;
