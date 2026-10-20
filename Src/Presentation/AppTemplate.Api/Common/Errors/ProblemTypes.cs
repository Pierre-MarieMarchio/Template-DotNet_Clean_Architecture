namespace AppTemplate.Api.Common.Errors;

internal static class ProblemTypes
{
    public const string DefaultBaseUri = "https://apptemplate.example/problems";

    public static string For(string code) => For(code, DefaultBaseUri);

    public static string For(string code, string baseUri)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(baseUri);

        return $"{baseUri.TrimEnd('/')}/{code}";
    }
}
