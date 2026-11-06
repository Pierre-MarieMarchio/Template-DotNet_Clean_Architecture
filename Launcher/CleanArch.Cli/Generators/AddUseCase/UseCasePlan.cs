using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddUseCase;

internal static class UseCasePlan
{
    internal static readonly Regex _tokenPattern = new("%([A-Za-z]+)%", RegexOptions.None, TimeSpan.FromSeconds(2));

    internal static Dictionary<string, string> Tokens(UseCaseSpec spec)
    {
        string result = spec.IsCommand ? "Result" : $"Result<{spec.Dto}>";
        string lambda = spec.IsCommand ? "command" : "query";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P"] = spec.Prefix,
            ["F"] = spec.Features,
            ["U"] = spec.Name,
            ["K"] = spec.Folder,
            ["In"] = spec.Input,
            ["in"] = lambda,
            ["D"] = spec.Dto ?? string.Empty,
            ["Dtos"] = spec.IsCommand ? string.Empty : $"{spec.Prefix}.Application.Features.{spec.Features}.Model.Dtos",
            ["Out"] = result,
            ["Kind"] = spec.IsCommand ? "Command" : "Query",
            ["Concurrency"] = spec.IsCommand && spec.HasId ? $"{spec.Prefix}.Application.Core.Common.Concurrency" : string.Empty,
            ["Message"] = Message(spec, result),
            ["Unwritten"] = Unwritten(spec),
            ["Rules"] = string.Join(
                "\n\n",
                spec.Ruled.Select(parameter =>
                    $"        RuleFor({lambda} => {lambda}.{parameter.Name})\n"
                    + $"            .NotEmpty().WithMessage(\"{parameter.Required}\");")),
            ["Refusals"] = string.Concat(
                spec.Ruled.Select(parameter =>
                    "\n"
                    + "    [Fact]\n"
                    + $"    public async Task AnEmpty{parameter.Name}_IsRefused() =>\n"
                    + $"        (await _validator.ValidateAsync(AWellFormed{(spec.IsCommand ? "Command" : "Query")}() with {{ {parameter.Name} = {parameter.Empty} }}, TestToken))\n"
                    + "            .IsValid.ShouldBeFalse();\n")),
            ["Values"] = string.Join(", ", spec.Parameters.Select(parameter => parameter.Valid)) is { Length: <= 60 } values
                ? values
                : "\n" + string.Join(",\n", spec.Parameters.Select(parameter => "            " + parameter.Valid)),
            ["Malformed"] = spec.Ruled.Length == 0 ? string.Empty : $"{spec.Ruled[0].Name} = {spec.Ruled[0].Empty}",
        };
    }

    internal static string Message(UseCaseSpec spec, string result)
    {
        string[] declared =
        [
            .. spec.Parameters.Select(parameter => $"{parameter.Type} {parameter.Name}"),
            .. spec.IsCommand && spec.HasId ? new[] { "VersionPrecondition? Precondition = null" } : [],
        ];

        return Record(spec.Input, declared, $" : I{(spec.IsCommand ? "Command" : "Query")}<{result}>");
    }

    internal static string Unwritten(UseCaseSpec spec) => (spec.IsCommand, spec.HasId) switch
    {
        (true, true) => $"\"{spec.Name} is not written yet: load the {spec.Aggregate}, call the method that holds the rule, then commit through IUnitOfWork.\"",
        (true, false) => $"\"{spec.Name} is not written yet: decide, then commit through IUnitOfWork.\"",
        _ => $"\"{spec.Name} is not written yet: read through the feature's queries port into {spec.Dto}.\"",
    };

    internal static string Render(string template, Dictionary<string, string> tokens)
    {
        string text = _tokenPattern.Replace(
            template,
            match => tokens.TryGetValue(match.Groups[1].Value, out string? value) ? value : match.Value);

        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        int usings = lines.TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal)).Count();

        string[] sorted = [.. lines[..usings]
            .Where(line => line != "using ;")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line.StartsWith("using System", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(line => line.TrimEnd(';'), StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.TrimEnd(';'), StringComparer.Ordinal)];

        return string.Join('\n', [.. sorted, .. lines[usings..]]).TrimEnd('\n') + "\n";
    }

    internal static List<Emission> Plan(UseCaseSpec spec, string root, out string? skippedTests)
    {
        var tokens = Tokens(spec);
        var emissions = new List<Emission>();

        string application = $"{SourceProject(spec.Prefix, "Application")}/Features/{spec.Features}";
        string folder = $"{application}/UseCases/{spec.Folder}/{spec.Name}";

        void Add(string path, string template) => emissions.Add(new Emission(path, Render(template, tokens)));

        Add($"{folder}/{spec.Name}.cs", UseCaseTemplates.UseCase);

        if (spec.HasValidator)
        {
            Add($"{folder}/{spec.Input}Validator.cs", UseCaseTemplates.Validator);
        }

        if (spec.Dto is not null && !spec.DtoExists)
        {
            Add($"{application}/Model/Dtos/{spec.Dto}.cs", spec.HasId ? UseCaseTemplates.DtoWithId : UseCaseTemplates.Dto);
        }

        skippedTests = null;

        if (spec.HasValidator)
        {
            string tests = $"Tests/Application/{FolderOf(spec.Prefix, "Application.UnitTests")}";
            string useCaseTests = $"{tests}/Features/{spec.Features}/UseCases/{spec.Folder}/{spec.Name}";

            if (Directory.Exists(Path.Combine(root, tests)))
            {
                Add($"{useCaseTests}/{spec.Input}ValidatorTests.cs", UseCaseTemplates.ValidatorTests);

                if (File.Exists(Path.Combine(root, tests, "TestDoubles", "ValidatedHandler.cs")))
                {
                    Add($"{useCaseTests}/{spec.Name}Tests.cs", UseCaseTemplates.HandlerTests);
                }
            }
            else
            {
                skippedTests = tests;
            }
        }

        return emissions;
    }

    internal static string Sentence(string pascal)
    {
        string spoken = CSharpNames.Separated(pascal, ' ');

        return char.ToUpperInvariant(spoken[0]) + spoken[1..];
    }

    internal static string Record(string type, IEnumerable<string> declared, string implemented)
    {
        string[] members = [.. declared];

        return members.Length switch
        {
            0 => $"public sealed record {type}{implemented};",
            1 => $"public sealed record {type}({members[0]}){implemented};",
            _ => $"public sealed record {type}(\n{string.Join(",\n", members.Select(member => "    " + member))}){implemented};",
        };
    }
}
