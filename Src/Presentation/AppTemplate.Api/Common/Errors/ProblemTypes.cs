namespace AppTemplate.Api.Common.Errors;

/// <summary>
/// The <c>type</c> member of a <c>ProblemDetails</c> response.
/// </summary>
/// <remarks>
/// RFC 9457 §3.1: <c>type</c> identifies the problem, not the HTTP status it happens to map to. A
/// literal <c>https://httpstatuses.io/{status}</c> fails that: two 400s with different causes would
/// share one URI. Deriving the URI from the stable <c>code</c> instead means two different problems
/// never collide, and the same problem always resolves to the same URI.
/// </remarks>
public static class ProblemTypes
{
    /// <summary>Used wherever no <see cref="ProblemTypeOptions.BaseUri"/> is reachable.</summary>
    public const string DefaultBaseUri = "https://apptemplate.example/problems";

    public static string For(string code) => For(code, DefaultBaseUri);

    public static string For(string code, string baseUri)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(baseUri);

        return $"{baseUri.TrimEnd('/')}/{code}";
    }
}
