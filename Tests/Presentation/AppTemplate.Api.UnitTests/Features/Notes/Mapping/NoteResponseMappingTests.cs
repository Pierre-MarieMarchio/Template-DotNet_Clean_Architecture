using AppTemplate.Api.Features.Notes.Mapping;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Model.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Notes.Mapping;

/// <summary>A hand-written mapper's only failure mode is a field nobody copied, so each test asserts the whole shape.</summary>
public sealed class NoteResponseMappingTests
{
    private static readonly DateTimeOffset _createdAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public void ToResponse_Note_CopiesEveryField()
    {
        var note = new NoteDto(Guid.CreateVersion7(), "Groceries", "Milk", _createdAt, _createdAt.AddDays(1));

        var response = NoteResponseMapping.ToResponse(note);

        response.Id.ShouldBe(note.Id);
        response.Title.ShouldBe(note.Title);
        response.Body.ShouldBe(note.Body);
        response.CreatedAt.ShouldBe(note.CreatedAt);
        response.LastModifiedAt.ShouldBe(note.LastModifiedAt);
    }

    [Fact]
    public void ToResponse_Summary_CopiesEveryField_AndCarriesNoBody()
    {
        var summary = new NoteSummaryDto(Guid.CreateVersion7(), "Groceries", _createdAt, null);

        var response = NoteResponseMapping.ToResponse(summary);

        response.Id.ShouldBe(summary.Id);
        response.Title.ShouldBe(summary.Title);
        response.CreatedAt.ShouldBe(summary.CreatedAt);
        response.LastModifiedAt.ShouldBeNull();
    }

    [Fact]
    public void ToNoteResponse_KeepsTheVersionBesideTheRepresentation()
    {
        var note = new NoteDto(Guid.CreateVersion7(), "Groceries", "Milk", _createdAt, null);

        var mapped = NoteResponseMapping.ToNoteResponse(new Versioned<NoteDto>(note, 42u));

        mapped.Value.Version.ShouldBe(42u);
        mapped.Value.Value.Id.ShouldBe(note.Id);
    }

    [Fact]
    public void ToNoteResponse_CarriesAFailureThroughUnchanged()
    {
        var error = NoteErrors.NoteNotFound(Guid.CreateVersion7());

        var mapped = NoteResponseMapping.ToNoteResponse(Result.Failure<Versioned<NoteDto>>(error));

        mapped.Error.ShouldBe(error);
    }

    [Fact]
    public void ToPageResponse_CarriesThePagingMetadataAcross()
    {
        var page = PagedResult.Offset<NoteSummaryDto>(
            [new(Guid.CreateVersion7(), "Groceries", _createdAt, null)], page: 2, pageSize: 10, totalCount: 11);

        var mapped = NoteResponseMapping.ToPageResponse(page).Value;

        mapped.Items.Count.ShouldBe(1);
        mapped.Page.ShouldBe(2);
        mapped.PageSize.ShouldBe(10);
        mapped.TotalCount.ShouldBe(11);
        mapped.TotalPages.ShouldBe(2);
    }
}
