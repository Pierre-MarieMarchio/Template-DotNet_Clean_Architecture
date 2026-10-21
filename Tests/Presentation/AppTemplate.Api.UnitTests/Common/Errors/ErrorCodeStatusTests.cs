using System.Globalization;
using System.Reflection;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Errors;

public sealed class ErrorCodeStatusTests
{
    [Fact]
    public void EveryErrorOfAnErrorsClass_AnswersTheStatusItsRowStates()
    {
        var rows = DocumentedStatuses();
        var errors = DeclaredErrors().ToList();

        errors.ShouldNotBeEmpty("No *Errors class was found, so the walk is not reading the assemblies it describes.");

        string[] offenders =
        [
            .. errors
                .Select(declared => (declared.Origin, declared.Error.Code, Status: StatusOf(declared.Error)))
                .Select(error => (error.Origin, error.Code, error.Status, Documented: rows.GetValueOrDefault(error.Code, "no row")))
                .Where(error => error.Documented != "any" && error.Documented != error.Status)
                .Select(error => $"{error.Origin}: {error.Code} answers {error.Status}, the reference says {error.Documented}"),
        ];

        offenders.ShouldBeEmpty(
            "A client reads the status and the code together; a refusal that answers another status than " +
            "its row, or has no row, breaks the contract the reference states.");
    }

    private static string StatusOf(Error error) =>
        ((ObjectResult)error.ToActionResult()).StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "none";

    private static IEnumerable<(string Origin, Error Error)> DeclaredErrors()
    {
        var api = typeof(ErrorMapping).Assembly;
        string product = api.GetName().Name![..^".Api".Length];

        var assemblies = api.GetReferencedAssemblies()
            .Where(name => name.Name!.StartsWith(product + ".", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .Append(api);

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: true, IsSealed: true } && type.Name.EndsWith("Errors", StringComparison.Ordinal)))
        {
            const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var field in type.GetFields(members).Where(field => field.FieldType == typeof(Error)))
            {
                yield return ($"{type.Name}.{field.Name}", (Error)field.GetValue(null)!);
            }

            foreach (var property in type.GetProperties(members).Where(property => property.PropertyType == typeof(Error)))
            {
                yield return ($"{type.Name}.{property.Name}", (Error)property.GetValue(null)!);
            }

            foreach (var method in type.GetMethods(members).Where(method => method.ReturnType == typeof(Error) && !method.IsSpecialName))
            {
                object?[] arguments = [.. method.GetParameters().Select(parameter => SampleOf(parameter.ParameterType))];

                yield return ($"{type.Name}.{method.Name}", (Error)method.Invoke(null, arguments)!);
            }
        }
    }

    private static object? SampleOf(Type type) =>
        type == typeof(string) ? "sample"
        : type == typeof(Guid) ? Guid.CreateVersion7()
        : type.IsValueType ? Activator.CreateInstance(type)
        : null;

    private static Dictionary<string, string> DocumentedStatuses()
    {
        string reference = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "reference", "api.md"));
        string codes = reference[reference.IndexOf("## Error codes", StringComparison.Ordinal)..];
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string[] cells in codes.Split('\n')
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|')))
        {
            foreach (string code in cells[1].Split(',').Select(cell => cell.Trim().Trim('`')))
            {
                rows[code] = cells[2].Trim();
            }
        }

        rows.ShouldNotBeEmpty("The error-code tables of docs/reference/api.md read as empty.");

        return rows;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docs", "reference", "api.md")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No docs/reference/api.md above {AppContext.BaseDirectory}.");
    }
}
