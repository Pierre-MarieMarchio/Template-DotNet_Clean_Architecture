using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

public interface IConfirmFileUploadUseCase
    : IUseCase<ConfirmFileUploadCommand, Result<Versioned<StoredFileDto>>>;
