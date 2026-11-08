using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Hosting;

public sealed class DevelopmentOnlyDatabaseTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void TheApi_RefusesToStartOutsideDevelopment(string environment)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment));

        var refusal = Should.Throw<InvalidOperationException>(() => factory.Services);

        refusal.Message.ShouldStartWith(
            "This project runs on SQLite, a development database",
            Case.Sensitive,
            "a SQLite project serves one process from one file, so nothing but Development may start it.");
    }
}
