
namespace CleanArch.Cli.Generators.Shared;

internal sealed record FeatureSpec(
    string Prefix,
    string Features,
    string Type,
    string Access,
    string Deletion,
    string[] Operations,
    string Model,
    string Concurrency,
    string? Reaction,
    bool HasApi,
    IReadOnlyList<Field>? Fields = null,
    bool TablePrefixes = false)
{
    internal bool Has(string operation) => Operations.Contains(operation, StringComparer.Ordinal);
}
