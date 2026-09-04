using AppTemplate.Application.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Results;

public sealed class ErrorTests
{
    /// <summary>
    /// The type is what the transport layer turns into a status code, so a factory wired
    /// to the wrong one silently changes every HTTP response built from it. Swapping any
    /// pair of these turns this red.
    /// </summary>
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

    /// <summary>
    /// Clients branch on the code, so two errors describing the same situation must be
    /// indistinguishable — that is what makes "not found" and "not yours" the same answer.
    /// </summary>
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

    /// <summary>
    /// The enum is a closed contract with the transport layer: adding a member is fine,
    /// renumbering the existing ones would silently repoint every persisted or logged
    /// value.
    /// </summary>
    #region Details

    [Fact]
    public void Validation_WithDetails_CarriesThemThrough()
    {
        var details = new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["A name is required."] };

        var error = Error.Validation("request.validationFailed", "One or more fields are invalid.", details);

        error.Details.ShouldBe(details);
    }

    [Fact]
    public void ErrorsWithoutDetails_AreStillEqual()
    {
        Error.NotFound("a.b", "m").ShouldBe(Error.NotFound("a.b", "m"));
    }

    /// <summary>
    /// The compiler-generated equality would compare <see cref="Error.Details"/> by reference: two
    /// errors built from separate dictionaries holding the same keys and values would count as
    /// different, which would make two validation failures over the same field look distinct.
    /// </summary>
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

    #endregion

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
