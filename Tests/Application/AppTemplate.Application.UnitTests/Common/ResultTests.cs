using AppTemplate.Application.Common;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common;

public sealed class ResultTests
{
    private static readonly Error _anError = Error.NotFound("thing.notFound", "No such thing.");

    #region The non-generic result

    [Fact]
    public void Success_IsSuccessfulAndCarriesNoError()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_IsAFailureAndCarriesTheError()
    {
        var result = Result.Failure(_anError);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_anError);
    }

    #endregion

    #region The generic result

    [Fact]
    public void Success_CarriesTheValue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBeNull();
        result.Value.ShouldBe(42);
    }

    /// <summary>
    /// A null slipping through here is served as a 200 with an empty body under a non-nullable
    /// declaration, which is indistinguishable from a real answer to the caller.
    /// </summary>
    [Fact]
    public void Success_RefusesANullValue()
    {
        Should.Throw<ArgumentNullException>(() => Result<string>.Success(null!));
    }

    [Fact]
    public void TheImplicitConversion_RefusesANullValue()
    {
        Should.Throw<ArgumentNullException>(() =>
        {
            Result<string> _ = (string)null!;
        });
    }

    [Fact]
    public void Success_AcceptsTheDefaultOfAValueType()
    {
        var result = Result<int>.Success(0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }

    /// <summary>
    /// Reading the value of a failure is a programming error, not a fallback: silently
    /// handing back <c>default</c> would let a failed use case look like one that returned
    /// zero rows.
    /// </summary>
    [Fact]
    public void Value_Throws_OnAFailure()
    {
        var result = Result<int>.Failure(_anError);

        var exception = Should.Throw<InvalidOperationException>(() => result.Value);

        exception.Message.ShouldContain("failed result");
    }

    [Fact]
    public void Value_Throws_OnAFailureCarryingAReferenceType() =>
        Should.Throw<InvalidOperationException>(() => Result<string>.Failure(_anError).Value);

    [Fact]
    public void Failure_CarriesTheErrorAndReportsFailure()
    {
        var result = Result<int>.Failure(_anError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_anError);
    }

    [Fact]
    public void AGenericResult_IsAlsoANonGenericResult() =>
        Result<int>.Success(1).ShouldBeAssignableTo<Result>();

    #endregion

    #region The type-inferring entry points

    [Fact]
    public void TheInferringSuccessFactory_ProducesASuccessCarryingTheValue()
    {
        var result = Result.Success("done");

        result.ShouldBeOfType<Result<string>>();
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("done");
    }

    [Fact]
    public void TheInferringFailureFactory_ProducesAFailure()
    {
        var result = Result.Failure<string>(_anError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_anError);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    /// <summary>
    /// The implicit conversion is what lets a use case end with <c>return value;</c>. It
    /// must produce a success, not a wrapper that still has to be unpacked.
    /// </summary>
    [Fact]
    public void AValue_ConvertsImplicitlyToASuccessfulResult()
    {
        Result<int> result = 7;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(7);
    }

    #endregion

    #region The construction guards

    /// <summary>
    /// A result carrying both a value and an error would leave every caller free to pick
    /// which one to believe. The constructor refuses to build one.
    /// </summary>
    [Fact]
    public void ASuccess_CannotBeConstructedWithAnError()
    {
        var exception = Should.Throw<InvalidOperationException>(() => new DerivedResult(isSuccess: true, _anError));

        exception.Message.ShouldContain("successful result");
    }

    /// <summary>
    /// A failure with no error would reach the transport layer with nothing to render, so
    /// it is refused at construction rather than dereferenced later.
    /// </summary>
    [Fact]
    public void AFailure_CannotBeConstructedWithoutAnError()
    {
        var exception = Should.Throw<InvalidOperationException>(() => new DerivedResult(isSuccess: false, error: null));

        exception.Message.ShouldContain("requires an error");
    }

    [Fact]
    public void TheValidCombinations_AreAccepted()
    {
        new DerivedResult(isSuccess: true, error: null).IsSuccess.ShouldBeTrue();
        new DerivedResult(isSuccess: false, _anError).IsFailure.ShouldBeTrue();
    }

    #endregion

    #region To<TOther>

    [Fact]
    public void To_ReportsTheSameFailure_UnderAnotherValueType()
    {
        var result = Result.Failure<int>(_anError);

        var converted = result.To<string>();

        converted.IsFailure.ShouldBeTrue();
        converted.Error.ShouldBe(_anError);
    }

    /// <summary>A success carries no error to report, so converting one is a programming error.</summary>
    [Fact]
    public void To_Throws_OnASuccess() =>
        Should.Throw<InvalidOperationException>(() => Result.Success(1).To<string>());

    [Fact]
    public void To_WorksFromTheNonGenericResultToo()
    {
        var result = Result.Failure(_anError);

        var converted = result.To<int>();

        converted.IsFailure.ShouldBeTrue();
        converted.Error.ShouldBe(_anError);
    }

    #endregion

    #region The failure-only Value

    /// <summary>
    /// Property subpatterns short-circuit in the order they are written. Naming <c>Value</c> before
    /// <c>IsSuccess</c> reads the getter before the guard runs, so it throws on a failure instead of
    /// simply not matching.
    /// </summary>
    [Fact]
    public void APropertyPatternNamingValueBeforeIsSuccess_ThrowsOnAFailure_InsteadOfFailingToMatch()
    {
        Result<int> result = Result<int>.Failure(_anError);

        // A discard ("Value: var _") is elided by the compiler and never calls the getter at all,
        // so the pattern has to bind the value to a real variable to force the read this test pins.
        Should.Throw<InvalidOperationException>(() =>
        {
            _ = result is { Value: var capturedValue, IsSuccess: true } && capturedValue >= 0;
        });
    }

    /// <summary>The safe order: the guard runs first and the pattern simply fails to match.</summary>
    [Fact]
    public void APropertyPatternNamingIsSuccessFirst_FailsToMatch_InsteadOfThrowing()
    {
        Result<int> result = Result<int>.Failure(_anError);

        (result is { IsSuccess: true, Value: var capturedValue } && capturedValue >= 0).ShouldBeFalse();
    }

    #endregion
}

/// <summary>
/// The guards live in a private protected constructor, so reaching them means deriving from
/// <c>Result</c> — which is exactly what any future result type in the layer would do.
/// </summary>
internal sealed class DerivedResult(bool isSuccess, Error? error) : Result(isSuccess, error);
