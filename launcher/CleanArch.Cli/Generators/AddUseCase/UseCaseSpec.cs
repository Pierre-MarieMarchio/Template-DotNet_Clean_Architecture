using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddUseCase;

internal sealed record UseCaseSpec(
    string Prefix,
    string Features,
    string Name,
    string Kind,
    string? Aggregate,
    IReadOnlyList<Parameter> Inputs,
    string? Dto,
    bool DtoExists,
    string? Manifest,
    Routing? Routing = null)
{
    internal bool IsCommand => Kind == "command";

    internal bool HasId => Aggregate is not null;

    internal string Folder => IsCommand ? "Commands" : "Queries";

    internal string Input => Name + (IsCommand ? "Command" : "Query");

    internal string Request => Name + "Request";

    internal string Namespace => $"{Prefix}.Application.Features.{Features}.UseCases.{Folder}.{Name}";

    internal IReadOnlyList<Parameter> Parameters =>
        Aggregate is null ? Inputs : [new Parameter($"{Aggregate}Id", "Guid", $"{Subject(Aggregate)} id is required."), .. Inputs];

    internal Parameter[] Ruled => [.. Parameters.Where(parameter => parameter.Empty is not null)];

    internal bool HasValidator => Ruled.Length > 0;

    internal bool HasRequest => Inputs.Count > 0;

    internal Parameter[] Unknown => [.. Inputs.Where(parameter => !parameter.IsKnown)];

    private static string Subject(string pascal)
    {
        string spoken = CSharpNames.Separated(pascal, ' ');

        return (CSharpNames.IsVowel(spoken[0]) ? "An " : "A ") + spoken;
    }
}
