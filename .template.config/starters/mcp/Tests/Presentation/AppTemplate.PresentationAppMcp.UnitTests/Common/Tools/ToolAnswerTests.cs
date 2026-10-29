using AppTemplate.Application.Core.Common.Results;
using AppTemplate.PresentationApp.Common.Tools;
using TextContentBlock = ModelContextProtocol.Protocol.TextContentBlock;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Common.Tools;

public sealed class ToolAnswerTests
{
    [Fact]
    public void ASuccess_AnswersItsValueAsStructuredContentAndAsText()
    {
        var answer = ToolAnswer.From(Result.Success(new Sample("Groceries", null)), sample => sample);

        answer.IsError.ShouldBeNull();
        answer.StructuredContent!.Value.GetProperty("title").GetString().ShouldBe("Groceries");
        answer.StructuredContent.Value.TryGetProperty("body", out _).ShouldBeFalse("A null is left out rather than sent.");
        answer.Content.OfType<TextContentBlock>().Single().Text.ShouldBe(answer.StructuredContent.Value.GetRawText());
    }

    [Fact]
    public void AFailure_IsAnErrorResultCarryingTheProblemsCodeAndDetail()
    {
        var answer = ToolAnswer.From(
            Result.Failure<Sample>(Error.NotFound("sample.notFound", "No such sample.")),
            sample => sample);

        answer.IsError.ShouldBe(true);
        answer.StructuredContent!.Value.GetProperty("code").GetString().ShouldBe("sample.notFound");
        answer.StructuredContent.Value.GetProperty("detail").GetString().ShouldBe("No such sample.");
        answer.Content.OfType<TextContentBlock>().Single().Text.ShouldContain("sample.notFound");
    }

    [Fact]
    public void AValidationFailure_CarriesItsFieldErrors()
    {
        var error = Error.Validation(
            "validation.failed",
            "The request is invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["Title"] = ["Title is required."] });

        var answer = ToolAnswer.Failure(error);

        answer.StructuredContent!.Value.GetProperty("errors").GetProperty("Title")[0].GetString().ShouldBe("Title is required.");
    }

    private sealed record Sample(string Title, string? Body);
}
