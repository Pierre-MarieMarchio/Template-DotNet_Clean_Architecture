using CleanArch.Tooling.Manifests;
using Shouldly;
using Xunit;

namespace CleanArch.Tooling.Tests.Manifests;

public sealed class ConditionTests
{
    private static readonly Dictionary<string, string> _values = new(StringComparer.Ordinal)
    {
        ["auth"] = "none",
        ["examples"] = "all",
        ["files"] = "true",
        ["web"] = "none",
    };

    [Theory]
    [InlineData("(auth == \"none\")", true)]
    [InlineData("(auth != \"none\")", false)]
    [InlineData("(files)", true)]
    [InlineData("(!files)", false)]
    [InlineData("(auth == \"none\" && (examples != \"none\" || files))", true)]
    [InlineData("auth == \"local\" || web == \"none\" && files", true)]
    [InlineData("(auth == \"local\" || web == \"none\") && !files", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("(unknown)", false)]
    [InlineData("(unknown != \"none\")", true)]
    public void IsTrue_ReadsTheTemplateEngineSyntax(string text, bool expected) =>
        Condition.Parse(text).IsTrue(_values.GetValueOrDefault).ShouldBe(expected);

    [Fact]
    public void Symbols_AreTheNamesTheConditionReads() =>
        Condition.Parse("(auth == \"none\" && (examples != \"none\" || files))").Symbols
            .Order(StringComparer.Ordinal)
            .ShouldBe(["auth", "examples", "files"]);

    [Theory]
    [InlineData("(auth == \"none\"")]
    [InlineData("auth == \"none")]
    [InlineData("auth ==")]
    [InlineData("auth none")]
    [InlineData("auth < 3")]
    public void Parse_RefusesWhatItCannotRead(string text) =>
        Should.Throw<FormatException>(() => Condition.Parse(text));
}
