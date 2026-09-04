namespace AppTemplate.Api.Common.Idempotency;

/// <summary>
/// Marks a POST action as safe to retry through an <c>Idempotency-Key</c> header: two identical
/// requests carrying the same key produce one effect, and the second gets back the first's response.
/// </summary>
/// <remarks>
/// Deliberately not applied to the authentication endpoints. Replaying a login would mean storing
/// the issued bearer token in the response the store keeps, so it could be handed back unchanged on
/// a retry — a credential at rest in a table whose entire purpose is to be read back by whoever can
/// query it. Every other state-changing POST is a candidate; a login is a credential mint, not a
/// mutation this pattern was built for.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute;
