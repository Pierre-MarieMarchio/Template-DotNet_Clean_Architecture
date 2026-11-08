namespace CleanArch.Tooling.Additions;

public sealed record WebJoinOptions(string? Framework = null, string? Audience = null, string? Rendering = null, string? Port = null)
{
    public IReadOnlyList<string> Given =>
        [.. new[] { ("--framework", Framework), ("--audience", Audience), ("--rendering", Rendering), ("--port", Port) }
            .Where(option => option.Item2 is not null)
            .Select(option => option.Item1)];
}
