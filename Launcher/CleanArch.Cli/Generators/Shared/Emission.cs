namespace CleanArch.Cli.Generators.Shared;

internal sealed record Emission(string Path, string Content)
{
    internal byte[] Bytes() => TextEdits.Encode(Path, Content);
}
