using AppTemplate.Application.Common.Validation;
using FluentValidation;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Validation;

public sealed class ValidationExtensionsTests
{
    private sealed record Request(string Name);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EnsureValidAsync_ReturnsSuccess_WhenTheRequestIsValid()
    {
        var validator = new InlineValidator<Request>();
        validator.RuleFor(request => request.Name).NotEmpty();

        var result = await validator.EnsureValidAsync(new Request("A name"), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureValidAsync_ReturnsTheValidationError_WhenTheRequestIsInvalid()
    {
        var validator = new InlineValidator<Request>();
        validator.RuleFor(request => request.Name).NotEmpty();

        var result = await validator.EnsureValidAsync(new Request(string.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();

        var error = result.Error;

        error.ShouldNotBeNull();
        error.Code.ShouldBe("request.validationFailed");
        error.Details.ShouldNotBeNull();
        error.Details.ShouldContainKey("name");
    }

    [Fact]
    public async Task EnsureValidAsync_Rejects_ANullValidator()
    {
        IValidator<Request> validator = null!;

        await Should.ThrowAsync<ArgumentNullException>(
            () => validator.EnsureValidAsync(new Request("A name"), TestToken));
    }
}
