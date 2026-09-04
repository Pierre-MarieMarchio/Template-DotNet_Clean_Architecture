namespace AppTemplate.Api.Common.Caching;

/// <summary>
/// Marks an action whose response must never be stored by any cache — RFC 6749 §5.1 requires
/// <c>Cache-Control: no-store</c> on every response that carries a token.
/// </summary>
/// <remarks>
/// It marks the actions that answer with a token — <c>AuthController.Login</c> and
/// <c>AuthController.Refresh</c>. Without it those responses carry no <c>Cache-Control</c> at all:
/// <see cref="CachePolicies"/> writes its default only for a GET or a HEAD, and a token is handed
/// back from a POST.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NoStoreAttribute : Attribute;
