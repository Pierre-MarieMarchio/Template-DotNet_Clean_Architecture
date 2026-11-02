using System.Reflection;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class TypeFacts
{
    private const string _initOnlyMarker = "System.Runtime.CompilerServices.IsExternalInit";

    internal static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        is not null;

    internal static bool IsInitOnly(MethodInfo setter) =>
        Array.Exists(
            setter.ReturnParameter.GetRequiredCustomModifiers(),
            modifier => string.Equals(modifier.FullName, _initOnlyMarker, StringComparison.Ordinal));

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

    internal static bool ReferencesAssembly(Assembly assembly, string simpleName) =>
        assembly.GetReferencedAssemblies()
            .Any(reference => string.Equals(reference.Name, simpleName, StringComparison.Ordinal));
}
