using System.Text.Json;
using System.Text.Json.Serialization;
using AppTemplate.Application.Core.Common.Results;
using ModelContextProtocol.Protocol;

namespace AppTemplate.PresentationAppMcp.Common.Tools;

internal static class ToolAnswer
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static CallToolResult From<TValue, TAnswer>(Result<TValue> result, Func<TValue, TAnswer> answer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(answer);

        return result.IsSuccess ? Success(answer(result.Value)) : Failure(result.Error!);
    }

    internal static CallToolResult Success<TAnswer>(TAnswer answer) => Answer(answer, isError: false);

    internal static CallToolResult Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return Answer(new ToolProblem(error.Code, error.Message, error.Details), isError: true);
    }

    private static CallToolResult Answer<TAnswer>(TAnswer answer, bool isError)
    {
        var content = JsonSerializer.SerializeToElement(answer, _json);

        return new CallToolResult
        {
            IsError = isError ? true : null,
            StructuredContent = content,
            Content = [new TextContentBlock { Text = content.GetRawText() }],
        };
    }

    private sealed record ToolProblem(
        string Code,
        string Detail,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? Errors);
}
