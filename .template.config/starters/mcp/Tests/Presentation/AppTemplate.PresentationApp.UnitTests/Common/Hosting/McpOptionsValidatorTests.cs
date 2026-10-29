using AppTemplate.PresentationApp.Common.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Common.Hosting;

public sealed class McpOptionsValidatorTests
{
    [Theory]
    [InlineData("https://tools.example.com/mcp")]
    [InlineData("http://localhost:5190/mcp")]
    public void TheAddressClientsConnectTo_IsAccepted(string resource) =>
        new McpOptionsValidator().Validate(null, new McpOptions { Resource = resource }).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("/mcp")]
    [InlineData("ftp://tools.example.com/mcp")]
    [InlineData("https://tools.example.com/")]
    [InlineData("https://tools.example.com/mcp?tenant=1")]
    [InlineData("https://tools.example.com/mcp#tools")]
    public void AnythingElse_IsRefused(string resource) =>
        new McpOptionsValidator().Validate(null, new McpOptions { Resource = resource }).Failed.ShouldBeTrue();
}
