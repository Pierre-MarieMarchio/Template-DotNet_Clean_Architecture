using System.Reflection;
using System.Runtime.CompilerServices;

namespace AppTemplate.Architecture.Tests.Fixtures;

/// <summary>
/// The handful of facts about a type that NetArchTest has no predicate for, read from reflection.
/// <para>
/// Each one exists because a rule needs it and the rule engine cannot express it: whether a
/// property's setter is an <c>init</c> accessor, whether a class is a record, and whether an
/// interface is reached through a base class rather than declared directly.
/// </para>
/// </summary>
internal static class TypeFacts
{
    private const string _initOnlyMarker = "System.Runtime.CompilerServices.IsExternalInit";

    /// <summary>
    /// A record is identified by its synthesised <c>&lt;Clone&gt;$</c> member, whose name is not
    /// expressible in C# and therefore cannot be forged by a non-record type.
    /// </summary>
    internal static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        is not null;

    /// <summary>
    /// An <c>init</c> accessor is a public setter as far as reflection is concerned, but it can only
    /// run inside an object initialiser, so it cannot mutate an existing instance. Telling the two
    /// apart is the difference between "immutable after construction" and "assignable by anyone".
    /// </summary>
    internal static bool IsInitOnly(MethodInfo setter) =>
        Array.Exists(
            setter.ReturnParameter.GetRequiredCustomModifiers(),
            modifier => string.Equals(modifier.FullName, _initOnlyMarker, StringComparison.Ordinal));

    /// <summary>
    /// Whether a type derives from a constructed form of an open generic base — <c>Entity&lt;&gt;</c>
    /// or <c>AggregateRoot&lt;&gt;</c>. NetArchTest's <c>Inherit</c> compares against a closed type,
    /// so it cannot ask this question.
    /// </summary>
    internal static bool DerivesFromOpenGeneric(Type type, Type openGeneric)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGeneric)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an assembly names another in its manifest. This is the linker's view of a dependency:
    /// present only if something in the assembly actually uses it, and therefore usable both to
    /// assert that a dependency exists and that one does not.
    /// </summary>
    internal static bool ReferencesAssembly(Assembly assembly, string simpleName) =>
        assembly.GetReferencedAssemblies()
            .Any(reference => string.Equals(reference.Name, simpleName, StringComparison.Ordinal));
}
