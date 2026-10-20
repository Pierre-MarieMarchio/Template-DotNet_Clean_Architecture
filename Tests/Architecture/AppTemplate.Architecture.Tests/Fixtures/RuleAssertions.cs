using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class RuleAssertions
{
    internal static IReadOnlyList<Type> RequireTypes(Assembly assembly)
    {
        var types = Types.InAssembly(assembly).GetTypes().ToList();

        types.ShouldNotBeEmpty(
            $"'{assembly.GetName().Name}' produced no types for NetArchTest to inspect, so every " +
            "rule written against it would pass for the wrong reason.");

        return types;
    }

    internal static IReadOnlyList<Type> RequireTypes(PredicateList predicate, string description)
    {
        var types = predicate.GetTypes().ToList();

        types.ShouldNotBeEmpty(
            $"No type matched '{description}', so the rule written against it would pass for the " +
            "wrong reason. Either the convention has been renamed or the rule is stale.");

        return types;
    }

    internal static void ShouldHold(this TestResult result, string because)
    {
        if (result.IsSuccessful)
        {
            return;
        }

        var offenders = result.FailingTypeNames ?? [];

        throw new ShouldAssertException(
            $"{because}{Environment.NewLine}Offending types:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", offenders));
    }

    internal static void ShouldDetectAViolation(this TestResult result, string because)
    {
        result.IsSuccessful.ShouldBeFalse(because);
        (result.FailingTypeNames ?? []).ShouldNotBeEmpty(because);
    }
}
