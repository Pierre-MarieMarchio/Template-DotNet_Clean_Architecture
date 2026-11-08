namespace AppTemplate.Application.Core.Common.Concurrency;

public sealed record Versioned<TValue>(TValue Value, uint Version) where TValue : notnull
{
    public TValue Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));
}
