using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Validation;
using FluentValidation.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Validation;

public sealed class ValidationErrorTests
{
    #region From

    [Fact]
    public void From_ProducesTheStableCodeAndAFixedMessage()
    {
        var validationResult = new ValidationResult([new ValidationFailure("Name", "A name is required.")]);

        var error = ValidationError.From(validationResult);

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("request.validationFailed");
        error.Message.ShouldBe("One or more fields are invalid.");
    }

    /// <summary>
    /// The message is fixed rather than a concatenation of the failures: two unrelated failures
    /// must not be able to change what the top-level message says.
    /// </summary>
    [Fact]
    public void From_NeverFoldsFailureMessagesIntoTheTopLevelMessage()
    {
        var validationResult = new ValidationResult(
        [
            new ValidationFailure("Name", "A name is required."),
            new ValidationFailure("Email", "Not a valid email address."),
        ]);

        var error = ValidationError.From(validationResult);

        error.Message.ShouldNotContain("required");
        error.Message.ShouldNotContain("email");
    }

    [Fact]
    public void From_PutsEachFailureMessageUnderItsCamelCasedField()
    {
        var validationResult = new ValidationResult([new ValidationFailure("Name", "A name is required.")]);

        var error = ValidationError.From(validationResult);

        var details = error.Details;

        details.ShouldNotBeNull();
        details.ShouldContainKey("name");
        details["name"].ShouldBe(["A name is required."]);
    }

    /// <summary>Several failures on the same field must all be kept, not just the first.</summary>
    [Fact]
    public void From_GroupsSeveralFailuresOnTheSameField()
    {
        var validationResult = new ValidationResult(
        [
            new ValidationFailure("Password", "Too short."),
            new ValidationFailure("Password", "Must contain a digit."),
        ]);

        var error = ValidationError.From(validationResult);

        error.Details.ShouldNotBeNull();
        error.Details["password"].ShouldBe(["Too short.", "Must contain a digit."]);
    }

    [Theory]
    [InlineData("Tags[0]", "tags[0]")]
    [InlineData("Address.City", "address.city")]
    [InlineData("Items[2].Name", "items[2].name")]
    public void From_CamelCasesEachSegmentOfAPropertyPath(string propertyName, string expectedKey)
    {
        var validationResult = new ValidationResult([new ValidationFailure(propertyName, "Invalid.")]);

        var error = ValidationError.From(validationResult);

        error.Details.ShouldNotBeNull();
        error.Details.ShouldContainKey(expectedKey);
    }

    [Fact]
    public void From_Rejects_ANullValidationResult() =>
        Should.Throw<ArgumentNullException>(() => ValidationError.From(null!));

    #endregion

    #region ForField

    /// <summary>For a rule no validator can express, e.g. one only the store can evaluate.</summary>
    [Fact]
    public void ForField_ProducesTheSameStableCodeAndFixedMessage()
    {
        var error = ValidationError.ForField("name", "That name is already taken.");

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("request.validationFailed");
        error.Message.ShouldBe("One or more fields are invalid.");
        error.Details.ShouldNotBeNull();
        error.Details["name"].ShouldBe(["That name is already taken."]);
    }

    [Fact]
    public void ForField_Rejects_ANullField() =>
        Should.Throw<ArgumentNullException>(() => ValidationError.ForField(null!, "m"));

    [Fact]
    public void ForField_Rejects_ANullMessage() =>
        Should.Throw<ArgumentNullException>(() => ValidationError.ForField("field", null!));

    #endregion
}
