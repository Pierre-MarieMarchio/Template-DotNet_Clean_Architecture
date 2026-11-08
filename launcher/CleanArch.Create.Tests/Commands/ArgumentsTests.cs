using CleanArch.Create.Commands;
using CleanArch.Tooling.Failures;
using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests.Commands;

public sealed class ArgumentsTests
{
    [Fact]
    public void Parse_SeparatesTheLaunchersOptionsFromTheTemplates()
    {
        Arguments parsed = Arguments.Parse(
            ["new", "-n", "Acme.Shop", "--output=out", "--auth", "none", "--files", "--yes", "--setup", "--web-audience=admin", "--template-source", "pkg.nupkg", "--hive", "hive"]);

        parsed.Name.ShouldBe("Acme.Shop");
        parsed.Output.ShouldBe("out");
        parsed.Yes.ShouldBeTrue();
        parsed.Setup.ShouldBeTrue();
        parsed.TemplateSource.ShouldBe("pkg.nupkg");
        parsed.Hive.ShouldBe("hive");
        parsed.TemplateOptions.ShouldBe(
            new Dictionary<string, string> { ["auth"] = "none", ["files"] = "true", ["web-audience"] = "admin" },
            ignoreOrder: true);
    }

    [Fact]
    public void Parse_IgnoresALeadingSeparator_WhichDnxMayPassOn()
    {
        Arguments parsed = Arguments.Parse(["--", "new", "-n", "Acme.Shop", "--yes"]);

        parsed.Name.ShouldBe("Acme.Shop");
        parsed.Yes.ShouldBeTrue();
        Arguments.Parse(["--", "--help"]).Help.ShouldBeTrue();
    }

    [Fact]
    public void Parse_KeepsEveryValueOfARepeatedTemplateOption() =>
        Arguments.Parse(["--presentation", "api", "--presentation=mcp", "--presentation", "worker"]).TemplateOptions["presentation"].ShouldBe("api|mcp|worker");

    [Theory]
    [InlineData("web-app")]
    [InlineData("web-package")]
    [InlineData("presentation")]
    public void Parse_SendsAnAdditionToTheProjectsTool(string addition) =>
        Should.Throw<LauncherException>(() => Arguments.Parse([addition])).Message.ShouldContain($"dotnet cleanarch add {addition}");

    [Theory]
    [InlineData("create")]
    [InlineData("-x")]
    [InlineData("-n")]
    public void Parse_RefusesWhatIsNotAnOption(string argument) =>
        Should.Throw<LauncherException>(() => Arguments.Parse([argument]));
}
