using System.Net;

namespace AppTemplate.EndToEnd.Tests;

internal static class Refusal
{
    internal const string Path = "api/v1/end-to-end-check";

    /*#if (accounts)*/
    internal const HttpStatusCode Status = HttpStatusCode.Unauthorized;

    internal const string Code = "auth.required";
    /*#else
    internal const HttpStatusCode Status = HttpStatusCode.NotFound;

    internal const string Code = "route.notFound";
    #endif*/
}
