using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace AppTemplate.Architecture.Tests.Fixtures;

/// <summary>
/// The two things a NetArchTest assertion needs in order to be worth writing: proof that it was
/// applied to something, and a failure message that names the offender.
/// <para>
/// A <c>ShouldNot()</c> condition over an empty type set succeeds. That makes a stale rule — one
/// whose namespace was renamed, or whose assembly was never loaded — indistinguishable from a
/// rule that holds. Every test in this project therefore establishes its candidate set first.
/// </para>
/// </summary>
internal static class RuleAssertions
{
    /// <summary>
    /// Asserts that an assembly yielded types for NetArchTest to inspect, and returns them.
    /// </summary>
    internal static IReadOnlyList<Type> RequireTypes(Assembly assembly)
    {
        var types = Types.InAssembly(assembly).GetTypes().ToList();

        types.ShouldNotBeEmpty(
            $"'{assembly.GetName().Name}' produced no types for NetArchTest to inspect, so every " +
            "rule written against it would pass for the wrong reason.");

        return types;
    }

    /// <summary>
    /// Asserts that a predicate matched at least one type, and returns the matches.
    /// </summary>
    /// <param name="predicate">The filtered type set a condition is about to be asserted over.</param>
    /// <param name="description">How the filter is meant to be read, used in the failure message.</param>
    internal static IReadOnlyList<Type> RequireTypes(PredicateList predicate, string description)
    {
        var types = predicate.GetTypes().ToList();

        types.ShouldNotBeEmpty(
            $"No type matched '{description}', so the rule written against it would pass for the " +
            "wrong reason. Either the convention has been renamed or the rule is stale.");

        return types;
    }

    /// <summary>
    /// Fails with the names of the offending types rather than a bare <c>false</c>.
    /// </summary>
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

    /// <summary>
    /// Asserts that a rule <em>does</em> detect a violation. Used to prove that the rule's
    /// matching machinery is live — a rule that cannot fail is not a guarantee.
    /// </summary>
    internal static void ShouldDetectAViolation(this TestResult result, string because)
    {
        result.IsSuccessful.ShouldBeFalse(because);
        (result.FailingTypeNames ?? []).ShouldNotBeEmpty(because);
    }
}
