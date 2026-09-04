namespace AppTemplate.Api.Common.Caching;

/// <summary>
/// Marks an action whose response must never be stored by any cache — RFC 6749 §5.1 requires
/// <c>Cache-Control: no-store</c> on every response that carries a token.
/// </summary>
/// <remarks>
/// Not applied to any action from here: the two candidates, <c>AuthController.Login</c> and
/// <c>AuthController.Refresh</c>, live under <c>Features/</c>, out of this pass's scope. The next
/// pass should add <c>[NoStore]</c> to both — today they get no <c>Cache-Control</c> at all, because
/// <see cref="CachePolicies"/> only ever wrote one for a GET or a HEAD.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NoStoreAttribute : Attribute;
