using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddField;

internal sealed record Addition(FeatureSpec Before, FeatureSpec After, IReadOnlyList<Field> Added, string Migration, FeatureManifest Manifest, bool Pending);

internal sealed record FieldChange(string Path, byte[]? Original, byte[] Bytes, List<string> Members);
