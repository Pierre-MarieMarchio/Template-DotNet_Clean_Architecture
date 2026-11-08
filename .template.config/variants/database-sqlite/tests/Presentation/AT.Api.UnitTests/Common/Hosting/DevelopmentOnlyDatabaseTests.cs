using AppTemplate.Api.Common.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Hosting;

public sealed class DevelopmentOnlyDatabaseTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("development-like")]
    public void AnyEnvironmentButDevelopment_IsRefused(string environment) =>
        Should.Throw<InvalidOperationException>(() =>
                DevelopmentOnlyDatabase.RefuseOutsideDevelopment(new HostEnvironment(environment)))
            .Message.ShouldBe(DevelopmentOnlyDatabase.Refusal);

    [Fact]
    public void Development_IsAccepted() =>
        Should.NotThrow(() => DevelopmentOnlyDatabase.RefuseOutsideDevelopment(new HostEnvironment(Environments.Development)));

    private sealed class HostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "AppTemplate.Api.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
