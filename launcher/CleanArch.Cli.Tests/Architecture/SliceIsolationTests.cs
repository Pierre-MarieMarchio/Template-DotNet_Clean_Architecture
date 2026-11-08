using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests.Architecture;

public sealed class SliceIsolationTests
{
    [Theory]
    [InlineData("CleanArch.Cli.Commands", "CleanArch.Cli.Commands", "Kernel", "Hosting")]
    [InlineData("CleanArch.Cli", "CleanArch.Cli.Generators", "Shared", null)]
    public void ASlice_ReachesNoOtherSlice_AndWhatTheSlicesShareReachesNone(string assemblyName, string root, string? shared, string? composer)
    {
        Assembly assembly = Assembly.Load(assemblyName);
        string[] slices = [.. ChildNamespaces(assembly, root).Where(child => child != shared && child != composer)];

        slices.Length.ShouldBeGreaterThanOrEqualTo(2, $"Fewer than two slices were found under {root}, so this rule compares nothing.");

        List<string> offenders = [.. slices.SelectMany(slice => Offenders(assembly, root, slice, slices.Where(other => other != slice)))];
        if (shared is not null)
        {
            offenders.AddRange(Offenders(assembly, root, shared, composer is null ? slices : [.. slices, composer]));
        }

        offenders.ShouldBeEmpty(
            $"Each slice under {root} stands alone, so that it can be read, moved, ejected or removed without the others. "
            + SharedRule(shared, composer)
            + "Move what two slices need into what they share.");
    }

    private static IEnumerable<string> Offenders(Assembly assembly, string root, string subject, IEnumerable<string> forbidden)
    {
        PredicateList subjects = Types.InAssembly(assembly).That().ResideInNamespaceMatching($@"^{Regex.Escape($"{root}.{subject}")}(\.|$)");
        subjects.GetTypes().ShouldNotBeEmpty($"No type was found in {root}.{subject}, so it is not a subject.");

        return subjects.ShouldNot()
            .HaveDependencyOnAny([.. forbidden.Select(other => $"{root}.{other}")])
            .GetResult()
            .FailingTypeNames?
            .Select(type => $"{subject}: {type}") ?? [];
    }

    private static string SharedRule(string? shared, string? composer)
    {
        if (shared is null)
        {
            return string.Empty;
        }

        return composer is null
            ? $"{shared}, which they share, reaches none of them. "
            : $"{shared}, which they share, reaches none of them nor {composer}. ";
    }

    private static IEnumerable<string> ChildNamespaces(Assembly assembly, string root) =>
        assembly.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith(root + ".", StringComparison.Ordinal))
            .Select(ns => ns[(root.Length + 1)..].Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
}
