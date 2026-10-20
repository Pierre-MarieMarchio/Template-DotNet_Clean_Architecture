namespace AppTemplate.Api.Common.Idempotency;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute;
