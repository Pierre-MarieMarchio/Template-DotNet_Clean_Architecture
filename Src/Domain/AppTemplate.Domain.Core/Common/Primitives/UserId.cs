using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Core.Common.Primitives;

/// <summary>
/// Who a thing belongs to, as this domain knows them: an identifier supplied from outside, with no
/// attribute of a person behind it.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>User</c> entity here, because the business owns no fact about a person. What it
/// needs is the identity a caller was authenticated as, and this is that and nothing more — no
/// foreign key to any identity table, no navigation, and nothing about how the caller proved it. It
/// is also exactly what a separate authentication service hands over, which is a subject claim, so
/// the name stays accurate on the day the module that mints one moves out.
/// </para>
/// <para>
/// A type rather than a <see cref="Guid"/>, so that an aggregate's own id cannot be passed where an
/// owner is expected: the two are both <see cref="Guid"/> and neither reads differently at a call
/// site. An empty value is refused, which is what lets everything past this point treat a
/// <see cref="UserId"/> as naming somebody.
/// </para>
/// </remarks>
public sealed record UserId
{
    private UserId(Guid value) => Value = value;

    /// <summary>The identifier itself, for a persistence row or a claim to carry.</summary>
    public Guid Value { get; }

    /// <summary>Refuses an empty identifier, which would name no owner.</summary>
    /// <param name="value">The identifier a caller was authenticated as.</param>
    /// <returns>The owner that identifier names.</returns>
    /// <exception cref="DomainException">The value is empty.</exception>
    public static UserId Create(Guid value) =>
        value == Guid.Empty
            ? throw new DomainException("A user id cannot be empty: it would name no owner.")
            : new UserId(value);

    /// <summary>
    /// The same rule where having no owner is a legitimate answer rather than a failure — an
    /// anonymous request, or a claim the caller never presented.
    /// </summary>
    /// <param name="value">The identifier, or <see langword="null"/> when there is none.</param>
    /// <returns>The owner, or <see langword="null"/> when there is nobody to name.</returns>
    public static UserId? CreateOptional(Guid? value) =>
        value is { } id && id != Guid.Empty ? new UserId(id) : null;

    /// <summary>The identifier, so a log line or a cache key reads as one.</summary>
    /// <returns>The value in its canonical form.</returns>
    public override string ToString() => Value.ToString();
}
