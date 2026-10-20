using AppTemplate.Application.Core.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Results;

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

    [Fact]
    public void Success_RefusesANullValue() => Should.Throw<ArgumentNullException>(() => Result<string>.Success(null!));

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

    [Fact]
    public void AValue_ConvertsImplicitlyToASuccessfulResult()
    {
        Result<int> result = 7;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(7);
    }

    #endregion

    #region The construction guards

    [Fact]
    public void ASuccess_CannotBeConstructedWithAnError()
    {
        var exception = Should.Throw<InvalidOperationException>(() => new DerivedResult(isSuccess: true, _anError));

        exception.Message.ShouldContain("successful result");
    }

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

    [Fact]
    public void APropertyPatternNamingValueBeforeIsSuccess_ThrowsOnAFailure_InsteadOfFailingToMatch()
    {
        var result = Result<int>.Failure(_anError);

        Should.Throw<InvalidOperationException>(() =>
        {
            _ = result is { Value: var capturedValue, IsSuccess: true } && capturedValue >= 0;
        });
    }

    [Fact]
    public void APropertyPatternNamingIsSuccessFirst_FailsToMatch_InsteadOfThrowing()
    {
        var result = Result<int>.Failure(_anError);

        (result is { IsSuccess: true, Value: var capturedValue } && capturedValue >= 0).ShouldBeFalse();
    }

    #endregion
}

internal sealed class DerivedResult(bool isSuccess, Error? error) : Result(isSuccess, error);
