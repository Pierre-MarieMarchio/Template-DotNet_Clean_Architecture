using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Validation;

public sealed class ValidationErrorTests
{
    [Fact]
    public void From_ProducesTheStableCodeAndAFixedMessage()
    {
        var validationResult = new ValidationResult([new ValidationFailure("Name", "A name is required.")]);

        var error = ValidationError.From(validationResult);

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("request.validationFailed");
        error.Message.ShouldBe("One or more fields are invalid.");
    }

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

    [Fact]
    public void ForFields_CarriesEveryFieldUnderTheSameStableCodeAndFixedMessage()
    {
        var error = ValidationError.ForFields(new Dictionary<string, IReadOnlyList<string>>
        {
            ["userName"] = ["That user name is invalid."],
            ["password"] = ["Too short.", "Needs a digit."],
        });

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("request.validationFailed");
        error.Message.ShouldBe("One or more fields are invalid.");
        error.Details!["userName"].ShouldBe(["That user name is invalid."]);
        error.Details["password"].ShouldBe(["Too short.", "Needs a digit."]);
    }

    [Fact]
    public void ForFields_Rejects_NullDetails() =>
        Should.Throw<ArgumentNullException>(() => ValidationError.ForFields(null!));
}
