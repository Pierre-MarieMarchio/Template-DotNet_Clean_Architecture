using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests;

public sealed class TemplateEngineTests
{
    private const string _listing =
        """
        Currently installed items:
           Microsoft.DotNet.Web.ProjectTemplates.10.0
              Version: 10.0.13
           CleanArch.WebApi.Templates
              Version: 1.2.0
              Details:
                 Author: Pierre-MarieMarchio
        """;

    [Fact]
    public void InstalledVersion_IsReadFromTheUninstallListing()
    {
        TemplateEngine.InstalledVersion(_listing, "CleanArch.WebApi.Templates").ShouldBe("1.2.0");
        TemplateEngine.InstalledVersion(_listing, "CleanArch.Other").ShouldBeNull();
    }

    [Fact]
    public void CommandLine_QuotesOnlyWhatAShellWouldSplit() =>
        TemplateEngine.CommandLine("dotnet new", ["cleanarch-webapi", "-n", "Acme.Shop", "-o", "My Projects/shop"])
            .ShouldBe("dotnet new cleanarch-webapi -n Acme.Shop -o \"My Projects/shop\"");
}
