using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Notes.Model.Errors;

public sealed class NoteErrorsTests
{
    [Fact]
    public void NoteNotFound_IsANotFoundErrorNamingTheNote()
    {
        var noteId = Guid.CreateVersion7();

        var error = NoteErrors.NoteNotFound(noteId);

        error.Type.ShouldBe(ErrorType.NotFound);
        error.Code.ShouldBe("note.notFound");
        error.Message.ShouldContain(noteId.ToString());
    }
}
