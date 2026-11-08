using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddEvent;

internal sealed record EventSpec(
    string Prefix,
    string Features,
    string Name,
    IReadOnlyList<Field> Fields,
    string? ConsumerFeatures,
    string? Reaction,
    bool ConsumerTakesConfiguration,
    string? Unconsumed,
    string? Manifest)
{
    internal const string Suffix = "DomainEvent";

    internal string Base => Name[..^Suffix.Length];

    internal string? ConsumerName => Reaction is null ? null : $"{Reaction}On{Base}Consumer";

    internal string EventNamespace => $"{Prefix}.Domain.Features.{Features}.Model.Events";

    internal string ConsumerNamespace => $"{Prefix}.Application.Features.{ConsumerFeatures}.Services.Consumers.{Base}";
}
