namespace AppTemplate.Domain.Core.Common.Primitives;

/// <summary>
/// An object with a stable identity: two entities are the same entity when their ids are
/// equal, regardless of their other values.
/// </summary>
/// <typeparam name="TId">The identity type, which may not be null.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>Assigns the identity, which an entity never afterwards changes.</summary>
    /// <param name="id">The identity. Refused when null.</param>
    protected Entity(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    /// <summary>The identity equality is decided on.</summary>
    public TId Id { get; private init; }

    /// <summary>
    /// Equal when the runtime types match and the ids are equal. The type is part of it: two
    /// aggregates of different kinds that share an id are not the same thing.
    /// </summary>
    /// <param name="other">The entity to compare with.</param>
    /// <returns><see langword="true"/> when both name the same entity.</returns>
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>Identity equality, null-safe on both sides.</summary>
    /// <param name="left">The left entity, possibly null.</param>
    /// <param name="right">The right entity, possibly null.</param>
    /// <returns><see langword="true"/> when both name the same entity, or both are null.</returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>The negation of the equality operator.</summary>
    /// <param name="left">The left entity, possibly null.</param>
    /// <param name="right">The right entity, possibly null.</param>
    /// <returns><see langword="true"/> when the two name different entities.</returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
