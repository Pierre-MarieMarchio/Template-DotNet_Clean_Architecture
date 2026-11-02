using System.Reflection;

namespace AppTemplate.Api.Common.Hosting;

internal static class BuildTimeOpenApiDocument
{
    internal static bool IsBeingGenerated =>
        string.Equals(Assembly.GetEntryAssembly()?.GetName().Name, "GetDocument.Insider", StringComparison.Ordinal);
}
