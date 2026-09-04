using System.Reflection;
using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Files.Contracts.Requests;
using AppTemplate.Api.Features.Files.Contracts.Responses;
using AppTemplate.Api.Features.Files.Controllers;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;
using AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;
using AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;
using AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;
using AppTemplate.Domain.Features.Files.ValueObjects;
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

/// <summary>
/// The transport decisions this controller makes, each one asserted where it is actually taken.
/// </summary>
/// <remarks>
/// The use cases are substituted: what they decide is theirs to test. What is left here is the part
/// no other layer can be asked about — the status a redirect really carries, the entity tag a read
/// publishes, which header reaches which command, and which action opted into idempotency.
/// </remarks>
public sealed class FilesControllerTests
{
    private static readonly DateTimeOffset _registeredAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly IGetStoredFilesUseCase _getStoredFiles = Substitute.For<IGetStoredFilesUseCase>();
    private readonly IGetStoredFileUseCase _getStoredFile = Substitute.For<IGetStoredFileUseCase>();
    private readonly IIssueFileDownloadUseCase _issueFileDownload = Substitute.For<IIssueFileDownloadUseCase>();
    private readonly IRegisterFileUseCase _registerFile = Substitute.For<IRegisterFileUseCase>();
    private readonly IConfirmFileUploadUseCase _confirmFileUpload = Substitute.For<IConfirmFileUploadUseCase>();
    private readonly IDeleteStoredFileUseCase _deleteStoredFile = Substitute.For<IDeleteStoredFileUseCase>();

    #region Listing

    /// <summary>
    /// Both records are seven positional members of two types, so swapping <c>Search</c> and
    /// <c>State</c> — or <c>Page</c> and <c>PageSize</c> — compiles and ships. Every value here is
    /// distinct so that a permutation cannot pass.
    /// </summary>
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

    /// <summary>
    /// A page of files is not one aggregate, so there is no single version describing it — and a
    /// validator published over one would be a promise about rows a later page has never seen.
    /// </summary>
    [Fact]
    public async Task GetAll_PublishesNoEntityTag()
    {
        var httpContext = AContext();

        _getStoredFiles.ExecuteAsync(Arg.Any<GetStoredFilesQuery>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<StoredFileDto>(
                [AFile(StoredFileState.Available)],
                page: 1,
                pageSize: 20,
                totalCount: 1));

        var action = await AController(httpContext).GetAll(
            new GetStoredFilesRequest(null, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        action.Result.ShouldBeOfType<OkObjectResult>();
        httpContext.Response.Headers.ETag.Count.ShouldBe(0);
    }

    #endregion

    #region Reading content is a redirect, and the redirect is not cacheable

    /// <summary>
    /// Executed through MVC's own <see cref="RedirectResultExecutor"/> rather than asserted on the
    /// <see cref="RedirectResult"/>'s properties: 302 versus 307 is a decision the executor makes
    /// from <see cref="RedirectResult.Permanent"/> and <see cref="RedirectResult.PreserveMethod"/>
    /// together, so a test naming the status itself would be asserting its own arithmetic rather than
    /// the framework's.
    /// </summary>
    [Fact]
    public async Task GetContent_AnswersA302_WhoseLocationIsTheSignedUrl()
    {
        const string url = "https://store.example/bucket/t0/2026/03/abcdef?sig=deadbeef";
        var httpContext = AContext();

        _issueFileDownload.ExecuteAsync(Arg.Any<IssueFileDownloadQuery>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedDownloadGrant(url, _registeredAt.AddMinutes(5)));

        var action = await AController(httpContext).GetContent(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        await ExecuteAsync(action, httpContext);

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        httpContext.Response.Headers.Location.ToString().ShouldBe(url);
    }

    /// <summary>
    /// The <c>Location</c> is a bearer credential with minutes to live, so the response must not be
    /// storable. The attribute is what <c>CacheHeaderExtensions</c> reads to write
    /// <c>Cache-Control: no-store</c> instead of the <c>private, no-cache</c> every other read gets —
    /// and <c>no-cache</c> permits storage, which is exactly the failure: a stored redirect outlives
    /// the grant it names.
    /// </summary>
    [Fact]
    public void EveryActionCarryingACredential_IsNoStore()
    {
        ActionsWith<NoStoreAttribute>().ShouldBe(
            [nameof(FilesController.GetContent), nameof(FilesController.Register)],
            "GetContent answers with a signed read URL in its Location, and Register answers with a "
            + "signed write URL in its body. Those two responses carry a credential; the others "
            + "carry metadata and take this API's ordinary caching contract.");
    }

    [Fact]
    public async Task GetContent_AnswersLikeAnAbsentFile_WhenTheFileIsNotTheCallers()
    {
        var fileId = Guid.CreateVersion7();

        _issueFileDownload.ExecuteAsync(Arg.Any<IssueFileDownloadQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IssuedDownloadGrant>(StoredFileErrors.FileNotFound(fileId)));

        var action = await AController().GetContent(fileId, TestContext.Current.CancellationToken);

        StatusOf(action).ShouldBe(
            StatusCodes.Status404NotFound,
            "the application layer answers not-found for somebody else's file so that comparing 403 "
            + "against 404 cannot enumerate other users' ids. Mapping it to anything else here would "
            + "undo that at the last step.");
    }

    [Fact]
    public async Task GetContent_Answers409_ForAFileWhoseDepositWasNeverConfirmed()
    {
        var fileId = Guid.CreateVersion7();

        _issueFileDownload.ExecuteAsync(Arg.Any<IssueFileDownloadQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IssuedDownloadGrant>(StoredFileErrors.FileNotAvailable(fileId)));

        var action = await AController().GetContent(fileId, TestContext.Current.CancellationToken);

        StatusOf(action).ShouldBe(StatusCodes.Status409Conflict);
    }

    #endregion

    #region Reads that name one file publish its version

    [Fact]
    public async Task GetById_PublishesTheVersionAsAStrongEntityTag()
    {
        var httpContext = AContext();

        _getStoredFile.ExecuteAsync(Arg.Any<GetStoredFileQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Versioned<StoredFileDto>(AFile(StoredFileState.Available), 1234u));

        var action = await AController(httpContext).GetFile(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        action.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<StoredFileResponse>();
        httpContext.Response.Headers.ETag.ToString().ShouldBe(EntityTagMapping.From(1234u));
    }

    [Fact]
    public async Task GetById_Answers304_WhenTheCallerAlreadyNamesThatVersion()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfNoneMatch = EntityTagMapping.From(1234u);

        _getStoredFile.ExecuteAsync(Arg.Any<GetStoredFileQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Versioned<StoredFileDto>(AFile(StoredFileState.Available), 1234u));

        var action = await AController(httpContext).GetFile(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status304NotModified);
        httpContext.Response.Headers.ETag.ToString().ShouldBe(
            EntityTagMapping.From(1234u),
            "RFC 9110 requires a 304 to carry the validator it is refusing to resend the body for.");
    }

    #endregion

    #region Writes on one file are conditional; creating one is not

    [Fact]
    public async Task Confirm_HandsTheVersionsFromIfMatch_ToTheUseCase()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = EntityTagMapping.From(77u);

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

    /// <summary>
    /// <c>If-Match: *</c> asserts the registration still exists, which is the question a client
    /// resuming a long upload is really asking — the abandonment sweep removes registrations nobody
    /// deposited against. A 404 would read as "wrong id"; 412 says "it was there and is not".
    /// </summary>
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
        httpContext.Request.Headers.IfMatch = EntityTagMapping.From(9u);

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

    #endregion

    #region Registering

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
    public async Task Register_MapsAQuotaRefusal_To409()
    {
        _registerFile.ExecuteAsync(Arg.Any<RegisterFileCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RegisterFileOutcome>(StoredFileErrors.QuotaExceeded("No room.")));

        var action = await AController().Register(ARequest(), TestContext.Current.CancellationToken);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Creating a file names no existing resource, so there is no version for a condition to be about
    /// — not even where a deployment requires <c>If-Match</c> on every write. A registration that
    /// answered 428 would be unreachable: the only endpoint that publishes a file's entity tag is the
    /// read of a file that does not exist yet.
    /// </summary>
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
        captured.Name.ShouldBe("quarterly-report.pdf");
        captured.MediaType.ShouldBe("application/pdf");
        captured.SizeInBytes.ShouldBe(1024L);
        captured.Checksum.ShouldBe(new string('a', 64));
    }

    #endregion

    #region What the surface deliberately does not do

    /// <summary>
    /// Registering is unaddressed creation, so a retry is indistinguishable from a second request and
    /// replaying it mints a second file and a second signed grant. Confirming names the file in its
    /// route and can only ever happen once, so the resource's own identity already carries what a key
    /// would buy.
    /// </summary>
    [Fact]
    public void RegisteringIsTheOnlyIdempotentAction()
    {
        ActionsWith<IdempotentAttribute>().ShouldBe([nameof(FilesController.Register)]);
    }

    /// <summary>
    /// The bodies here are metadata — a name, a media type, a length, a digest — and the file's bytes
    /// never enter the request pipeline. So no action needs the 64 KiB inbound cap widened, and an
    /// attribute doing so would be the first sign somebody had started routing content through this
    /// process after all.
    /// </summary>
    [Fact]
    public void NoAction_WidensTheInboundBodyLimit()
    {
        string[] offenders =
        [
            .. ActionsWith<RequestSizeLimitAttribute>(),
            .. ActionsWith<DisableRequestSizeLimitAttribute>(),
            .. ActionsWith<RequestFormLimitsAttribute>(),
        ];

        offenders.ShouldBeEmpty();
    }

    #endregion

    #region Fixture

    private static RegisterFileRequest ARequest() =>
        new("quarterly-report.pdf", "application/pdf", 1024, new string('a', 64));

    private static IssuedUploadGrant AGrant() =>
        new(
            "https://store.example/bucket/t0/2026/03/abcdef?sig=cafebabe",
            "PUT",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "application/pdf" },
            _registeredAt.AddMinutes(30));

    private static StoredFileDto AFile(StoredFileState state) =>
        new(
            Guid.CreateVersion7(),
            "quarterly-report.pdf",
            "application/pdf",
            SizeInBytes: 1024,
            Checksum: new string('a', 64),
            state,
            _registeredAt,
            state == StoredFileState.Available ? _registeredAt.AddMinutes(1) : null);

    /// <summary>
    /// Not <c>HttpContextFactory</c>: this class runs an <see cref="ActionResult"/> through MVC's own
    /// executor, which resolves <see cref="IActionResultExecutor{TResult}"/> from the request's
    /// services, and no other test in this project needs that.
    /// </summary>
    private static DefaultHttpContext AContext(IfMatchRequirement ifMatch = IfMatchRequirement.Optional)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(Options.Create(new ProblemTypeOptions { BaseUri = ProblemTypes.DefaultBaseUri }));
        services.AddSingleton(Options.Create(new ConcurrencyOptions { IfMatch = ifMatch }));
        services.AddSingleton<IUrlHelperFactory, UrlHelperFactory>();
        services.AddSingleton<IActionResultExecutor<RedirectResult>, RedirectResultExecutor>();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            TraceIdentifier = $"trace-{Guid.NewGuid():N}",
        };
    }

    private FilesController AController(HttpContext? httpContext = null) =>
        new(_getStoredFiles, _getStoredFile, _issueFileDownload, _registerFile, _confirmFileUpload, _deleteStoredFile)
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
        var actions = typeof(FilesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToList();

        actions.Count.ShouldBe(
            6,
            "This controller no longer has the six actions every attribute rule in this class is "
            + "written against, so those rules have stopped describing it.");

        return
        [
            .. actions
                .Where(method => method.GetCustomAttribute<TAttribute>() is not null)
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    #endregion
}
