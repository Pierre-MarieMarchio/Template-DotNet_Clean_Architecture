using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;

public interface IGetStoredFileUseCase : IUseCase<GetStoredFileQuery, Result<Versioned<StoredFileDto>>>;
