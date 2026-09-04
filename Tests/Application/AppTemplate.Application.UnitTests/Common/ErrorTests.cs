using AppTemplate.Application.Common;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common;

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
