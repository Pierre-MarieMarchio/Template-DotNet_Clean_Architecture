using System.Reflection;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Notes.Contracts.Requests;
using AppTemplate.Api.Features.Notes.Contracts.Responses;
using AppTemplate.Api.Features.Notes.Controllers;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Application.Features.Notes.Errors;
using AppTemplate.Application.Features.Notes.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Notes.Controllers;

/// <summary>
/// The transport decisions this controller makes, each one asserted where it is taken. The service is
/// substituted: what it decides is its own to test.
/// </summary>
public sealed class NotesControllerTests
{
    private static readonly DateTimeOffset _createdAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly INoteService _notes = Substitute.For<INoteService>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #region Listing

    /// <summary>Five positional members of two types: every value is distinct so a permutation cannot pass.</summary>
    [Fact]
    public async Task GetAll_HandsEveryQueryStringParameter_ToTheService()
    {
        NoteListQueryDto? captured = null;

        _notes.ListAsync(Arg.Do<NoteListQueryDto>(query => captured = query), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<NoteSummaryDto>([], page: 1, pageSize: 20, totalCount: 0));

        await AController().GetAll(new GetNotesRequest("cursor", 3, 45, "an-opaque-token", "title:asc"), Token);

        captured.ShouldBe(new NoteListQueryDto("cursor", 3, 45, "an-opaque-token", "title:asc"));
    }

    [Fact]
    public async Task GetAll_PublishesNoEntityTag()
    {
        var httpContext = AContext();

        _notes.ListAsync(Arg.Any<NoteListQueryDto>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<NoteSummaryDto>([new(Guid.CreateVersion7(), "A", _createdAt, null)], 1, 20, 1));

        var action = await AController(httpContext).GetAll(new GetNotesRequest(null, null, null, null, null), Token);

        action.Result.ShouldBeOfType<OkObjectResult>();
        httpContext.Response.Headers.ETag.Count.ShouldBe(0);
    }

    #endregion

    #region A single note publishes its version

    [Fact]
    public async Task GetById_PublishesTheVersionAsAStrongEntityTag()
    {
        var httpContext = AContext();
        GivenTheServiceReads(version: 1234u);

        var action = await AController(httpContext).GetById(Guid.CreateVersion7(), Token);

        action.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<NoteResponse>();

        string tag = httpContext.Response.Headers.ETag.ToString();

        tag.ShouldStartWith("\"");
        tag.ShouldEndWith("\"");
        tag.ShouldNotBe(await APublishedEntityTagAsync(1235u));
    }

    [Fact]
    public async Task GetById_Answers304_WhenTheCallerAlreadyNamesThatVersion()
    {
        string published = await APublishedEntityTagAsync(1234u);
        var httpContext = AContext();
        httpContext.Request.Headers.IfNoneMatch = published;
        GivenTheServiceReads(version: 1234u);

        var action = await AController(httpContext).GetById(Guid.CreateVersion7(), Token);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status304NotModified);
        httpContext.Response.Headers.ETag.ToString().ShouldBe(published);
    }

    [Fact]
    public async Task GetById_Answers404_ForAMissingNote()
    {
        var noteId = Guid.CreateVersion7();
        _notes.GetAsync(noteId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Versioned<NoteDto>>(NoteErrors.NoteNotFound(noteId)));

        var action = await AController().GetById(noteId, Token);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status404NotFound);
    }

    #endregion

    #region Creating

    [Fact]
    public async Task Create_Answers201_LocatingTheNote_AndPublishingItsVersion()
    {
        var httpContext = AContext();
        var note = ANote();
        NoteContentDto? captured = null;

        _notes.CreateAsync(Arg.Do<NoteContentDto>(content => captured = content), Arg.Any<CancellationToken>())
            .Returns(new Versioned<NoteDto>(note, 1u));

        var action = await AController(httpContext).Create(new CreateNoteRequest("Groceries", "Milk"), Token);

        var created = action.Result.ShouldBeOfType<CreatedAtRouteResult>();

        created.StatusCode.ShouldBe(StatusCodes.Status201Created);
        created.RouteName.ShouldBe(nameof(NotesController.GetById));
        created.RouteValues!["noteId"].ShouldBe(note.Id);
        created.DeclaredType.ShouldBe(typeof(NoteResponse));
        httpContext.Response.Headers.ETag.ToString().ShouldNotBeNullOrEmpty();
        captured.ShouldBe(new NoteContentDto("Groceries", "Milk"));
    }

    /// <summary>
    /// Creating names no existing resource, so there is no version for a condition to be about, even
    /// where a deployment requires <c>If-Match</c> on every write.
    /// </summary>
    [Fact]
    public async Task Create_IsUnconditional_EvenWhereTheDeploymentRequiresAPrecondition()
    {
        _notes.CreateAsync(Arg.Any<NoteContentDto>(), Arg.Any<CancellationToken>())
            .Returns(new Versioned<NoteDto>(ANote(), 1u));

        var action = await AController(AContext(IfMatchRequirement.Required))
            .Create(new CreateNoteRequest("Groceries", "Milk"), Token);

        action.Result.ShouldBeOfType<CreatedAtRouteResult>();
    }

    [Fact]
    public async Task Create_MapsAValidationFailure_To400()
    {
        _notes.CreateAsync(Arg.Any<NoteContentDto>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Versioned<NoteDto>>(Error.Validation("request.validationFailed", "no")));

        var action = await AController().Create(new CreateNoteRequest("", "Milk"), Token);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status400BadRequest);
    }

    #endregion

    #region Updating and deleting honour If-Match

    [Fact]
    public async Task Update_HandsTheRouteIdAndTheVersionsFromIfMatch_ToTheService_AndPublishesTheNewTag()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = await APublishedEntityTagAsync(77u);
        var noteId = Guid.CreateVersion7();
        VersionPrecondition? captured = null;

        _notes.UpdateAsync(
                noteId,
                Arg.Any<NoteContentDto>(),
                Arg.Do<VersionPrecondition?>(precondition => captured = precondition),
                Arg.Any<CancellationToken>())
            .Returns(new Versioned<NoteDto>(ANote(noteId), 78u));

        var action = await AController(httpContext).Update(noteId, new UpdateNoteRequest("Groceries", "Milk"), Token);

        action.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<NoteResponse>();
        captured.ShouldNotBeNull().AcceptableVersions.ShouldBe([77u]);
        httpContext.Response.Headers.ETag.ToString().ShouldBe(await APublishedEntityTagAsync(78u));
    }

    [Fact]
    public async Task Update_WithoutIfMatch_IsUnconditional_WhereThePolicyAllowsIt()
    {
        VersionPrecondition? captured = new([1u]);

        _notes.UpdateAsync(Arg.Any<Guid>(), Arg.Any<NoteContentDto>(), Arg.Do<VersionPrecondition?>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(new Versioned<NoteDto>(ANote(), 2u));

        await AController().Update(Guid.CreateVersion7(), new UpdateNoteRequest("Groceries", "Milk"), Token);

        captured.ShouldBeNull();
    }

    [Fact]
    public async Task Update_Answers412_WhenIfMatchStarNamesANoteThatIsGone()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = "*";
        var noteId = Guid.CreateVersion7();
        _notes.UpdateAsync(noteId, Arg.Any<NoteContentDto>(), Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Versioned<NoteDto>>(NoteErrors.NoteNotFound(noteId)));

        var action = await AController(httpContext).Update(noteId, new UpdateNoteRequest("Groceries", "Milk"), Token);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status412PreconditionFailed);
    }

    [Fact]
    public async Task Update_MapsAStaleVersion_To412()
    {
        _notes.UpdateAsync(Arg.Any<Guid>(), Arg.Any<NoteContentDto>(), Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Versioned<NoteDto>>(ConcurrencyErrors.PreconditionFailed));

        var action = await AController().Update(Guid.CreateVersion7(), new UpdateNoteRequest("Groceries", "Milk"), Token);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status412PreconditionFailed);
    }

    [Fact]
    public async Task Update_Answers400_ForAnIfMatchThatIsNotAnEntityTagList()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = "not-a-quoted-tag";

        var action = await AController(httpContext)
            .Update(Guid.CreateVersion7(), new UpdateNoteRequest("Groceries", "Milk"), Token);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status400BadRequest);
        await _notes.DidNotReceive().UpdateAsync(
            Arg.Any<Guid>(), Arg.Any<NoteContentDto>(), Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_Answers428_WhereTheDeploymentRefusesUnconditionalWrites()
    {
        var action = await AController(AContext(IfMatchRequirement.Required))
            .Update(Guid.CreateVersion7(), new UpdateNoteRequest("Groceries", "Milk"), Token);

        StatusOf(action.Result!).ShouldBe(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public async Task Delete_HandsTheVersionsFromIfMatch_ToTheService_AndAnswers204()
    {
        var httpContext = AContext();
        httpContext.Request.Headers.IfMatch = await APublishedEntityTagAsync(9u);
        VersionPrecondition? captured = null;

        _notes.DeleteAsync(Arg.Any<Guid>(), Arg.Do<VersionPrecondition?>(p => captured = p), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var action = await AController(httpContext).Delete(Guid.CreateVersion7(), Token);

        action.ShouldBeOfType<NoContentResult>();
        captured.ShouldNotBeNull().AcceptableVersions.ShouldBe([9u]);
    }

    [Fact]
    public async Task Delete_Answers428_WhereTheDeploymentRefusesUnconditionalWrites()
    {
        var action = await AController(AContext(IfMatchRequirement.Required)).Delete(Guid.CreateVersion7(), Token);

        StatusOf(action).ShouldBe(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public async Task Delete_Answers404_ForAMissingNote()
    {
        var noteId = Guid.CreateVersion7();
        _notes.DeleteAsync(noteId, Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(NoteErrors.NoteNotFound(noteId)));

        var action = await AController().Delete(noteId, Token);

        StatusOf(action).ShouldBe(StatusCodes.Status404NotFound);
    }

    #endregion

    #region Surface

    /// <summary>
    /// Creating is unaddressed, so a retry is indistinguishable from a second request. Update and delete
    /// name the note in the route and carry <c>If-Match</c>, which is what makes their retries safe.
    /// </summary>
    [Fact]
    public void CreatingIsTheOnlyIdempotentAction() =>
        typeof(NotesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<IdempotentAttribute>() is not null)
            .Select(method => method.Name)
            .ShouldBe([nameof(NotesController.Create)]);

    #endregion

    private void GivenTheServiceReads(uint version) =>
        _notes.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Versioned<NoteDto>(ANote(), version));

    private static NoteDto ANote(Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), "Groceries", "Milk", _createdAt, null);

    private static DefaultHttpContext AContext(IfMatchRequirement ifMatch = IfMatchRequirement.Optional)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(Options.Create(new ProblemTypeOptions()));
        services.AddSingleton(Options.Create(new ConcurrencyOptions { IfMatch = ifMatch }));

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            TraceIdentifier = $"trace-{Guid.NewGuid():N}",
        };
    }

    /// <summary>
    /// The entity tag this API publishes for a version, obtained the way a caller obtains one: by reading
    /// it off a response, so the encoding is never restated here.
    /// </summary>
    private static async Task<string> APublishedEntityTagAsync(uint version)
    {
        var httpContext = AContext();
        var reader = Substitute.For<INoteService>();
        reader.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Versioned<NoteDto>(ANote(), version));

        await new NotesController(reader) { ControllerContext = new ControllerContext { HttpContext = httpContext } }
            .GetById(Guid.CreateVersion7(), Token);

        return httpContext.Response.Headers.ETag.ToString();
    }

    private NotesController AController(HttpContext? httpContext = null) =>
        new(_notes)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext ?? AContext() },
        };

    private static int StatusOf(ActionResult action) => action switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? throw new InvalidOperationException(
            "The result carries no status code, so nothing can be asserted about it."),
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => throw new InvalidOperationException($"Unexpected result type {action.GetType().Name}."),
    };
}
