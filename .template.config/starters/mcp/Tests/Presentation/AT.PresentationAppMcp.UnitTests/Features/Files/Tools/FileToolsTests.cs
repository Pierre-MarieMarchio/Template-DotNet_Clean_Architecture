using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.PresentationAppMcp.Features.Files.Tools;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppMcp.UnitTests.Features.Files.Tools;

public sealed class FileToolsTests
{
    private static readonly StoredFileDto _file = new(
        Guid.CreateVersion7(),
        "documents",
        "invoice.pdf",
        "application/pdf",
        1024,
        "checksum",
        StoredFileState.Available,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        []);

    private readonly IGetStoredFilesUseCase _getStoredFiles = Substitute.For<IGetStoredFilesUseCase>();

    private readonly IGetStoredFileUseCase _getStoredFile = Substitute.For<IGetStoredFileUseCase>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private FileTools Tools => new(_getStoredFiles, _getStoredFile);

    [Fact]
    public async Task ListFiles_AsksForThePageItWasGiven()
    {
        _getStoredFiles.ExecuteAsync(Arg.Any<GetStoredFilesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PagedResult.Offset<StoredFileDto>([_file], 1, 20, 1)));

        var answer = await Tools.ListFilesAsync(1, 20, Cancellation);

        answer.StructuredContent!.Value.GetProperty("items")[0].GetProperty("name").GetString().ShouldBe("invoice.pdf");
        await _getStoredFiles.Received(1).ExecuteAsync(GetStoredFilesQuery.Offset(1, 20), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFile_AnswersTheFilesDetails_NeverItsContent()
    {
        _getStoredFile.ExecuteAsync(new GetStoredFileQuery(_file.Id), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Versioned<StoredFileDto>(_file, 1)));

        var answer = await Tools.GetFileAsync(_file.Id, Cancellation);

        answer.StructuredContent!.Value.GetProperty("state").GetString().ShouldBe("available");
    }
}
