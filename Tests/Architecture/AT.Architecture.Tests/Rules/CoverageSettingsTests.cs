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

        string[] excluding = [.. stateMachine
            .GetCustomAttributesData()
            .Select(attribute => attribute.AttributeType.FullName ?? attribute.AttributeType.Name)
            .Where(attribute => ExcludePatterns("Attribute").Any(pattern => Regex.IsMatch(attribute, pattern)))];

        excluding.ShouldBeEmpty(
            "the compiler moves an async method's whole body into a state machine type and marks "
            + "that type with these attributes; excluding one of them drops every async body from "
            + "the coverage report, so a file whose code is all async is never measured at all.");
    }

    private static async Task AwaitsOnce() => await Task.Yield();

    private static Type StateMachineOf(string method) =>
        typeof(CoverageSettingsTests)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetCustomAttribute<AsyncStateMachineAttribute>()!
            .StateMachineType;

    private static List<string> ExcludePatterns(string element) =>
        [.. XDocument
            .Load(_path)
            .Descendants(element)
            .Where(filter => filter.Parent?.Name.LocalName == "Exclude")
            .Select(filter => filter.Value.Trim())];
}
