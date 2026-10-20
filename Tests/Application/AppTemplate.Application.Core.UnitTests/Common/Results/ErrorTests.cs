using AppTemplate.Application.Core.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Results;

public sealed class ErrorTests
{
    [Fact]
    public void EachFactory_ProducesItsOwnErrorType()
    {
        Error.Validation("c", "m").Type.ShouldBe(ErrorType.Validation);
        Error.NotFound("c", "m").Type.ShouldBe(ErrorType.NotFound);
        Error.Unauthorized("c", "m").Type.ShouldBe(ErrorType.Unauthorized);
        Error.Forbidden("c", "m").Type.ShouldBe(ErrorType.Forbidden);
        Error.Conflict("c", "m").Type.ShouldBe(ErrorType.Conflict);
        Error.PreconditionFailed("c", "m").Type.ShouldBe(ErrorType.PreconditionFailed);
        Error.PreconditionRequired("c", "m").Type.ShouldBe(ErrorType.PreconditionRequired);
    }

    [Fact]
    public void AFactory_PassesTheCodeAndMessageThrough()
    {
        var error = Error.Validation("todoList.validationFailed", "A list name is required.");

        error.Code.ShouldBe("todoList.validationFailed");
        error.Message.ShouldBe("A list name is required.");
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Error.NotFound("a.b", "m").ShouldBe(Error.NotFound("a.b", "m"));
        Error.NotFound("a.b", "m").GetHashCode().ShouldBe(Error.NotFound("a.b", "m").GetHashCode());
    }

    [Fact]
    public void Equality_DistinguishesTheCodeTheMessageAndTheType()
    {
        var reference = Error.NotFound("a.b", "m");

        reference.ShouldNotBe(Error.NotFound("a.c", "m"));
        reference.ShouldNotBe(Error.NotFound("a.b", "other"));
        reference.ShouldNotBe(Error.Conflict("a.b", "m"));
    }

    [Fact]
    public void Validation_WithDetails_CarriesThemThrough()
    {
        var details = new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["A name is required."] };

        var error = Error.Validation("request.validationFailed", "One or more fields are invalid.", details);

        error.Details.ShouldBe(details);
    }

    [Fact]
    public void ErrorsWithoutDetails_AreStillEqual() => Error.NotFound("a.b", "m").ShouldBe(Error.NotFound("a.b", "m"));

    [Fact]
    public void Equality_ComparesDetailsStructurally_NotByReference()
    {
        var first = Error.Validation(
            "request.validationFailed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["required"] });

        var second = Error.Validation(
            "request.validationFailed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["required"] });

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equality_DistinguishesDifferentDetails()
    {
        var first = Error.Validation(
            "request.validationFailed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["required"] });

        var second = Error.Validation(
            "request.validationFailed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["too long"] });

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Equality_DistinguishesNoDetailsFromDetails()
    {
        var withoutDetails = Error.Validation("request.validationFailed", "One or more fields are invalid.");

        var withDetails = Error.Validation(
            "request.validationFailed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["required"] });

        withoutDetails.ShouldNotBe(withDetails);
    }

    [Fact]
    public void TheErrorTypes_KeepTheirDeclaredOrder() =>
        Enum.GetValues<ErrorType>().ShouldBe(
        [
            ErrorType.Validation,
            ErrorType.NotFound,
            ErrorType.Unauthorized,
            ErrorType.Forbidden,
            ErrorType.Conflict,
            ErrorType.TooManyRequests,
            ErrorType.PreconditionFailed,
            ErrorType.PreconditionRequired,
        ]);
}
