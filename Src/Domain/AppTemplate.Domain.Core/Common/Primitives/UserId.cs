using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Core.Common.Primitives;

public sealed record UserId
{
    private UserId(Guid value) => Value = value;

    public Guid Value { get; }

    public static UserId Create(Guid value) =>
        value == Guid.Empty
            ? throw new DomainException("A user id cannot be empty: it would name no owner.")
            : new UserId(value);

    public static UserId? CreateOptional(Guid? value) =>
        value is { } id && id != Guid.Empty ? new UserId(id) : null;

    public override string ToString() => Value.ToString();
}
