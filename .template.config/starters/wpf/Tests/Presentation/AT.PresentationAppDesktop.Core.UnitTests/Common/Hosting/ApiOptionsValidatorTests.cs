using AppTemplate.PresentationAppDesktop.Core.Common.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppDesktop.Core.UnitTests.Common.Hosting;

public sealed class ApiOptionsValidatorTests
{
    [Theory]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://api.example.com/")]
    public void AnAbsoluteHttpAddress_IsValid(string address) =>
        new ApiOptionsValidator().Validate(null, new ApiOptions { BaseAddress = new Uri(address) }).Succeeded.ShouldBeTrue();

    [Fact]
    public void NoAddress_IsRefused() =>
        new ApiOptionsValidator().Validate(null, new ApiOptions()).Failed.ShouldBeTrue();

    [Theory]
    [InlineData("/api/")]
    [InlineData("ftp://api.example.com/")]
    public void ARelativeOrNonHttpAddress_IsRefused(string address) =>
        new ApiOptionsValidator().Validate(null, new ApiOptions { BaseAddress = new Uri(address, UriKind.RelativeOrAbsolute) }).Failed.ShouldBeTrue();
}
