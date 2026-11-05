using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class CoverageSettingsTests
{
    private static readonly string _path = Path.Combine(
        ProjectReferenceGraph.RepositoryRoot, "coverage.runsettings");

    [Fact]
    public void AnAsyncMethodsBody_IsMeasured()
    {
        Type stateMachine = StateMachineOf(nameof(AwaitsOnce));

        ExcludingAttributes(stateMachine, ExcludePatterns("Attribute")).ShouldBeEmpty(
            "the compiler moves an async method's whole body into a state machine type and marks "
            + "that type with these attributes; excluding one of them drops every async body from "
            + "the coverage report, so a file whose code is all async is never measured at all. "
            + "Remove the attribute's pattern from the Exclude/Attribute list of coverage.runsettings.");
    }

    [Fact]
    public void TheCheck_IsSensitive_AndSeesAnExcludedStateMachineAttribute()
    {
        Type stateMachine = StateMachineOf(nameof(AwaitsOnce));

        ExcludingAttributes(stateMachine, ["^System\\.Runtime\\.CompilerServices\\.CompilerGeneratedAttribute$"])
            .ShouldBe(["System.Runtime.CompilerServices.CompilerGeneratedAttribute"]);
        ExcludingAttributes(stateMachine, ["^Some\\.Other\\.Attribute$"]).ShouldBeEmpty();
        ExcludingAttributes(stateMachine, []).ShouldBeEmpty();

        var settings = XDocument.Parse(
            "<RunSettings><Exclude><Attribute>A</Attribute></Exclude><Include><Attribute>B</Attribute></Include></RunSettings>");

        ExcludePatternsIn(settings, "Attribute").ShouldBe(["A"]);
    }

    [Theory]
    [InlineData("/home/runner/work/_temp/generated/Src/Domain/P.Domain/Things/Thing.cs")]
    [InlineData("/srv/Migrations/obj/Generated/checkout/Src/Application/P.Application/Features/Things/Thing.cs")]
    [InlineData(@"C:\Users\me\Generated\Migrations\Src\Infrastructure\P.Infrastructure.Persistence\Things\ThingStore.cs")]
    public void ASourceOfTheProject_IsMeasured_WhateverTheFoldersItIsCheckedOutUnder(string source)
    {
        ExcludingSources(source, ExcludePatterns("Source")).ShouldBeEmpty(
            "the collector matches a source's full path without case, so a pattern on a bare folder name such as "
            + "Generated, Migrations or obj also matches a folder the project sits in, and leaves every file out of "
            + "the report. Anchor the pattern below the project in the Exclude/Source list of coverage.runsettings.");
    }

    [Theory]
    [InlineData("/home/me/app/Src/Presentation/P.Presentation.Client/Generated/Models/LoginRequest.cs")]
    [InlineData("/home/me/app/Src/Infrastructure/P.Infrastructure.Auth/Migrations/20261103_InitialCreate.cs")]
    [InlineData("/home/me/app/Src/Application/P.Application/obj/Release/net10.0/Mediator.g.cs")]
    [InlineData(@"C:\app\Src\Presentation\P.Desktop\obj\Debug\net10.0-windows\App.g.cs")]
    public void TheCodeAToolWrote_IsLeftOut(string source)
    {
        ExcludingSources(source, ExcludePatterns("Source")).ShouldNotBeEmpty(
            "this file is written by a tool, which coverage.runsettings leaves out of the report.");
    }

    private static string[] ExcludingSources(string source, IEnumerable<string> patterns) =>
        [.. patterns.Where(pattern => Regex.IsMatch(source, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5)))];

    private static string[] ExcludingAttributes(Type stateMachine, IEnumerable<string> patterns) =>
        [.. stateMachine
            .GetCustomAttributesData()
            .Select(attribute => attribute.AttributeType.FullName ?? attribute.AttributeType.Name)
            .Where(attribute => patterns.Any(pattern => Regex.IsMatch(attribute, pattern, RegexOptions.None, TimeSpan.FromSeconds(5))))];

    private static async Task AwaitsOnce() => await Task.Yield();

    private static Type StateMachineOf(string method) =>
        typeof(CoverageSettingsTests)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetCustomAttribute<AsyncStateMachineAttribute>()!
            .StateMachineType;

    private static List<string> ExcludePatterns(string element) => ExcludePatternsIn(XDocument.Load(_path), element);

    private static List<string> ExcludePatternsIn(XDocument document, string element) =>
        [.. document
            .Descendants(element)
            .Where(filter => filter.Parent?.Name.LocalName == "Exclude")
            .Select(filter => filter.Value.Trim())];
}
