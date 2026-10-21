using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests;

public sealed class ArgumentsTests
{
    [Fact]
    public void Parse_SeparatesTheLaunchersOptionsFromTheTemplates()
    {
        Arguments parsed = Arguments.Parse(
            ["new", "-n", "Acme.Shop", "--output=out", "--auth", "none", "--files", "--yes", "--web-audience=admin", "--template-source", "pkg.nupkg", "--hive", "hive"]);

        parsed.Command.ShouldBe("new");
        parsed.Name.ShouldBe("Acme.Shop");
        parsed.Output.ShouldBe("out");
        parsed.Yes.ShouldBeTrue();
        parsed.TemplateSource.ShouldBe("pkg.nupkg");
        parsed.Hive.ShouldBe("hive");
        parsed.TemplateOptions.ShouldBe(
            new Dictionary<string, string> { ["auth"] = "none", ["files"] = "true", ["web-audience"] = "admin" },
            ignoreOrder: true);
    }

    [Fact]
    public void Parse_LeavesTheCommandToTheLauncher_WhenNoneIsGiven() =>
        Arguments.Parse(["--yes"]).Command.ShouldBeNull();

    [Theory]
    [InlineData("create")]
    [InlineData("-x")]
    [InlineData("-n")]
    public void Parse_RefusesWhatIsNotAnOption(string argument) =>
        Should.Throw<LauncherException>(() => Arguments.Parse([argument]));
}
