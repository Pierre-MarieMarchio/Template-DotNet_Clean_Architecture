using System.Reflection;
using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.Features.Files.Controllers;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;
using AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;
using AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Files.Controllers;

public sealed class FilesControllerTests
{
    private static readonly DateTimeOffset _registeredAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly IGetStoredFilesUseCase _getStoredFiles = Substitute.For<IGetStoredFilesUseCase>();
    private readonly IGetStoredFileUseCase _getStoredFile = Substitute.For<IGetStoredFileUseCase>();
    private readonly IIssueFileDownloadUseCase _issueFileDownload = Substitute.For<IIssueFileDownloadUseCase>();
    private readonly IRegisterFileUseCase _registerFile = Substitute.For<IRegisterFileUseCase>();
    private readonly IUploadFileUseCase _uploadFile = Substitute.For<IUploadFileUseCase>();
    private readonly IConfirmFileUploadUseCase _confirmFileUpload = Substitute.For<IConfirmFileUploadUseCase>();

    private readonly IDeleteStoredFileUseCase _deleteStoredFile = Substitute.For<IDeleteStoredFileUseCase>();

    [Fact]
    public async Task GetAll_HandsEveryQueryStringParameter_ToTheUseCase()
    {
        GetStoredFilesQuery? captured = null;

        _getStoredFiles
            .ExecuteAsync(Arg.Do<GetStoredFilesQuery>(query => captured = query), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<StoredFileDto>([], page: 1, pageSize: 20, totalCount: 0));

        var request = new GetStoredFilesRequest(
            Paging: "cursor",
            Page: 3,
            PageSize: 45,
            Cursor: "an-opaque-token",
            Sort: "name:asc",
            Search: "quarterly",
            State: "pending");

        await AController().GetAll(request, TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured.Paging.ShouldBe("cursor");
        captured.Page.ShouldBe(3);
        captured.PageSize.ShouldBe(45);
        captured.Cursor.ShouldBe("an-opaque-token");
        captured.Sort.ShouldBe("name:asc");
        captured.Search.ShouldBe("quarterly");
        captured.State.ShouldBe("pending");
    }

    [Fact]
    public async Task GetContent_AnswersA302_WhoseLocationIsTheSignedUrl()
    {
        const string url = "https://store.example/bucket/t0/2026/03/abcdef?sig=deadbeef";
        var httpContext = AContext();

        _issueFileDownload.ExecuteAsync(Arg.Any<IssueFileDownloadQuery>(), Arg.Any<CancellationToken>())
            .Returns(FileDownload.RedirectTo(
                new IssuedDownloadGrant(url, _registeredAt.AddMinutes(5)),
                "report.pdf",
                "application/pdf",
                3));

        var action = await AController(httpContext).GetContent(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        await ExecuteAsync(action, httpContext);

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        httpContext.Response.Headers.Location.ToString().ShouldBe(url);
    }

    [Fact]
    public async Task GetContent_StreamsTheBytesAsAnAttachment_WhenTheStoreCannotSign()
    {
        var httpContext = AContext();
        var content = new MemoryStream([1, 2, 3]);

        _issueFileDownload.ExecuteAsync(Arg.Any<IssueFileDownloadQuery>(), Arg.Any<CancellationToken>())
            .Returns(FileDownload.Streamed(content, "rapport été.pdf", "application/pdf", 3));

        var action = await AController(httpContext).GetContent(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        var file = action.ShouldBeOfType<FileStreamResult>();
        file.FileStream.ShouldBeSameAs(content);
        file.ContentType.ShouldBe("application/pdf");
        file.FileDownloadName.ShouldBeNullOrEmpty(
            "the disposition is written by DownloadDispositionPolicy, the same one a signed URL carries; "
            + "a FileDownloadName would make MVC write a second one.");
        httpContext.Response.Headers.ContentDisposition.ToString().ShouldBe(
            "attachment; filename=\"rapport _t_.pdf\"; filename*=UTF-8''rapport%20%C3%A9t%C3%A9.pdf");
        httpContext.Response.Headers.XContentTypeOptions.ToString().ShouldBe("nosniff");
    }

    [Fact]
    public void EveryActionCarryingACredential_IsNoStore()
    {
        ActionsWith<NoStoreAttribute>().ShouldBe(
            [nameof(FilesController.GetContent), nameof(FilesController.Register), nameof(FilesController.Upload)],
            "GetContent answers with a signed read URL in its Location, and Register answers with a "
            + "signed write URL in its body: those two carry a credential. Upload is a POST, which "
            + "gets no Cache-Control of its own. The others take this API's ordinary caching contract.");
    }

    [Fact]
    public async Task Confirm_HandsTheVersionsFromIfMatch_ToTheUseCase()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = await APublishedEntityTagAsync(77u);

        ConfirmFileUploadCommand? captured = null;

        _confirmFileUpload
            .ExecuteAsync(Arg.Do<ConfirmFileUploadCommand>(command => captured = command), Arg.Any<CancellationToken>())
            .Returns(new Versioned<StoredFileDto>(AFile(StoredFileState.Available), 78u));

        var fileId = Guid.CreateVersion7();

        await AController(httpContext).Confirm(fileId, TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured.StoredFileId.ShouldBe(fileId);
        captured.Precondition.ShouldNotBeNull().AcceptableVersions.ShouldBe([77u]);
    }

    [Fact]
    public async Task Confirm_Answers412_WhenIfMatchStarNamesARegistrationThatIsGone()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = "*";

        var fileId = Guid.CreateVersion7();

        _confirmFileUpload.ExecuteAsync(Arg.Any<ConfirmFileUploadCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Versioned<StoredFileDto>>(StoredFileErrors.FileNotFound(fileId)));

        var action = await AController(httpContext).Confirm(fileId, TestContext.Current.CancellationToken);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status412PreconditionFailed);
    }

    [Fact]
    public async Task Confirm_Answers400_ForAnIfMatchThatIsNotAnEntityTagList()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = "not-a-quoted-tag";

        var action = await AController(httpContext).Confirm(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status400BadRequest);
        await _confirmFileUpload.DidNotReceive()
            .ExecuteAsync(Arg.Any<ConfirmFileUploadCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_Answers428_WhereTheDeploymentRefusesUnconditionalWrites()
    {
        var httpContext = AContext(IfMatchRequirement.Required);

        var action = await AController(httpContext).Confirm(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public async Task Delete_HandsTheVersionsFromIfMatch_ToTheUseCase_AndAnswers204()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = await APublishedEntityTagAsync(9u);

        DeleteStoredFileCommand? captured = null;

        _deleteStoredFile
            .ExecuteAsync(Arg.Do<DeleteStoredFileCommand>(command => captured = command), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var action = await AController(httpContext).Delete(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        action.ShouldBeOfType<NoContentResult>();
        captured.ShouldNotBeNull();
        captured.Precondition.ShouldNotBeNull().AcceptableVersions.ShouldBe(
            [9u],
            "a file's bytes never change, but its state does — an unconditional delete of a file the "
            + "caller last saw as pending destroys content that arrived in between.");
    }

    [Fact]
    public async Task Register_Answers201_LocatingTheFileItCreated()
    {
        var storedFileId = Guid.CreateVersion7();

        _registerFile.ExecuteAsync(Arg.Any<RegisterFileCommand>(), Arg.Any<CancellationToken>())
            .Returns(new RegisterFileOutcome(storedFileId, AGrant()));

        var action = await AController().Register(ARequest(), TestContext.Current.CancellationToken);

        var created = action.Result.ShouldBeOfType<CreatedAtRouteResult>();

        created.StatusCode.ShouldBe(StatusCodes.Status201Created);
        created.RouteName.ShouldBe(nameof(FilesController.GetFile));
        created.RouteValues!["fileId"].ShouldBe(storedFileId);
        created.DeclaredType.ShouldBe(typeof(StoredFileRegistrationResponse));
        created.Value.ShouldBeOfType<StoredFileRegistrationResponse>().Upload.Url.ShouldBe(AGrant().Url);
    }

    [Fact]
    public async Task Register_IsUnconditional_EvenWhereTheDeploymentRequiresAPrecondition()
    {
        var httpContext = AContext(IfMatchRequirement.Required);

        _registerFile.ExecuteAsync(Arg.Any<RegisterFileCommand>(), Arg.Any<CancellationToken>())
            .Returns(new RegisterFileOutcome(Guid.CreateVersion7(), AGrant()));

        var action = await AController(httpContext).Register(ARequest(), TestContext.Current.CancellationToken);

        action.Result.ShouldBeOfType<CreatedAtRouteResult>();
    }

    [Fact]
    public async Task Register_PassesTheDeclaredMediaType_Unchanged()
    {
        RegisterFileCommand? captured = null;

        _registerFile
            .ExecuteAsync(Arg.Do<RegisterFileCommand>(command => captured = command), Arg.Any<CancellationToken>())
            .Returns(new RegisterFileOutcome(Guid.CreateVersion7(), AGrant()));

        await AController().Register(ARequest(), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured.Category.ShouldBe("document");
        captured.Name.ShouldBe("quarterly-report.pdf");
        captured.MediaType.ShouldBe("application/pdf");
        captured.SizeInBytes.ShouldBe(1024L);
        captured.Checksum.ShouldBe(new string('a', 64));
    }

    [Fact]
    public async Task Upload_Answers400OnTheFile_WhenThereIsNone()
    {
        var httpContext = AContext();

        var action = await AController(httpContext).Upload(
            AnUploadRequest(httpContext, file: null),
            TestContext.Current.CancellationToken);

        ShouldBeRefusedOnTheFile(action.Result!);
        await _uploadFile.DidNotReceive().ExecuteAsync(Arg.Any<UploadFileCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_Answers400OnTheFile_WhenThereAreTwo()
    {
        var httpContext = AContext();

        var action = await AController(httpContext).Upload(
            AnUploadRequest(httpContext, AFormFile("one.pdf"), extraFile: AFormFile("two.pdf")),
            TestContext.Current.CancellationToken);

        ShouldBeRefusedOnTheFile(action.Result!);
        await _uploadFile.DidNotReceive().ExecuteAsync(Arg.Any<UploadFileCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_MapsACategoryWithoutApiUploads_To409()
    {
        var httpContext = AContext();
        _uploadFile.ExecuteAsync(Arg.Any<UploadFileCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Versioned<StoredFileDto>>(StoredFileErrors.ApiUploadNotAllowed("document")));

        var action = await AController(httpContext).Upload(
            AnUploadRequest(httpContext, AFormFile("report.pdf")),
            TestContext.Current.CancellationToken);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void RegisteringIsTheOnlyIdempotentAction() =>
        ActionsWith<IdempotentAttribute>().ShouldBe([nameof(FilesController.Register)]);

    [Fact]
    public void OnlyTheUpload_WidensTheInboundBodyLimit()
    {
        ActionsWith<RequestBodyLimitAttribute>().ShouldBe([nameof(FilesController.Upload)]);

        string[] constantLimits =
        [
            .. ActionsWith<RequestSizeLimitAttribute>(),
            .. ActionsWith<DisableRequestSizeLimitAttribute>(),
            .. ActionsWith<RequestFormLimitsAttribute>(),
        ];

        constantLimits.ShouldBeEmpty();
    }

    private static RegisterFileRequest ARequest() =>
        new("document", "quarterly-report.pdf", "application/pdf", 1024, new string('a', 64));

    private static readonly byte[] _uploadedBytes = "%PDF-1.7 a small document"u8.ToArray();

    private static FormFile AFormFile(string fileName) =>
        new(new MemoryStream(_uploadedBytes), 0, _uploadedBytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "application/pdf" },
        };

    private static UploadFileRequest AnUploadRequest(
        HttpContext httpContext,
        IFormFile? file,
        string? name = null,
        IFormFile? extraFile = null)
    {
        var files = new FormFileCollection();

        if (file is not null)
        {
            files.Add(file);
        }

        if (extraFile is not null)
        {
            files.Add(extraFile);
        }

        httpContext.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(StringComparer.Ordinal)
            {
                ["category"] = "document",
            },
            files);

        return new UploadFileRequest("document", file, name);
    }

    private static void ShouldBeRefusedOnTheFile(ActionResult action)
    {
        var problem = action.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        problem.Value.ShouldBeOfType<ValidationProblemDetails>().Errors.Keys.ShouldContain("file");
    }

    private static IssuedUploadGrant AGrant() =>
        new(
            "https://store.example/bucket/t0/2026/03/abcdef?sig=cafebabe",
            "PUT",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/pdf" },
            _registeredAt.AddMinutes(30));

    private static StoredFileDto AFile(StoredFileState state) =>
        new(
            Guid.CreateVersion7(),
            "document",
            "quarterly-report.pdf",
            "application/pdf",
            SizeInBytes: 1024,
            Checksum: new string('a', 64),
            state,
            _registeredAt,
            state == StoredFileState.Available ? _registeredAt.AddMinutes(1) : null,
            Tags: []);

    private static DefaultHttpContext AContext(IfMatchRequirement ifMatch = IfMatchRequirement.Optional)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(Options.Create(new ProblemTypeOptions()));
        services.AddSingleton(Options.Create(new ConcurrencyOptions { IfMatch = ifMatch }));
        services.AddSingleton<IUrlHelperFactory, UrlHelperFactory>();
        services.AddSingleton<IActionResultExecutor<RedirectResult>, RedirectResultExecutor>();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            TraceIdentifier = $"trace-{Guid.NewGuid():N}",
        };
    }

    private async Task<string> APublishedEntityTagAsync(uint version)
    {
        var httpContext = AContext();

        _getStoredFile.ExecuteAsync(Arg.Any<GetStoredFileQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Versioned<StoredFileDto>(AFile(StoredFileState.Available), version));

        await AController(httpContext).GetFile(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        return httpContext.Response.Headers.ETag.ToString();
    }

    private FilesController AController(HttpContext? httpContext = null) =>
        new(
            _getStoredFiles,
            _getStoredFile,
            _issueFileDownload,
            _registerFile,
            _uploadFile,
            _confirmFileUpload,
            _deleteStoredFile)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext ?? AContext() },
        };

    private static Task ExecuteAsync(ActionResult action, HttpContext httpContext) =>
        action.ExecuteResultAsync(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));

    private static int StatusOf(ActionResult action) => action switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException(
            "The result carries no status code, so nothing can be asserted about it."),
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => throw new InvalidOperationException($"Unexpected result type {action.GetType().Name}."),
    };

    private static IReadOnlyList<string> ActionsWith<TAttribute>() where TAttribute : Attribute
    {
        var actions = new[] { typeof(FilesController), typeof(FileTagsController) }
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .ToList();

        actions.Count.ShouldBe(
            9,
            "The file surface no longer has the nine actions every attribute rule in this class is "
            + "written against, so those rules have stopped describing it.");

        return
        [
            .. actions
                .Where(method => method.GetCustomAttribute<TAttribute>() is not null)
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal),
        ];
    }
}
