using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Models;

/// <summary>
/// The account.
/// <para>
/// This is one of the two places where a persistence model has no domain twin, and needs none: it is a
/// framework type. <c>UserManager&lt;AppUser&gt;</c>, <c>SignInManager&lt;AppUser&gt;</c> and ASP.NET
/// Identity's own stores are all written against it, so a separate "domain user" would be a second model
/// of the same thing with no behaviour of its own to justify it. The to-do list aggregate gets a
/// persistence twin because it has invariants worth protecting from the schema; an account row does not.
/// </para>
/// <para>
/// Public because the identity module — which owns authentication policy but not the store —
/// has to name it in order to compose <c>UserManager&lt;AppUser&gt;</c>. Nothing else should.
/// </para>
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    /// <summary>
    /// Set once, from <c>IDateTimeProvider</c>, when the account is created. There is no
    /// <c>UpdatedAt</c> or <c>IsDeleted</c> column: a timestamp nothing writes after insert, and a
    /// flag nothing filters on, are worse than absent — an unenforced soft-delete flag would let a
    /// "deleted" user still authenticate.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The user's refresh-token grants. Internal, like <see cref="RefreshToken"/> itself: the
    /// grant chain is this assembly's business and no consumer has a reason to walk it. EF still
    /// maps the navigation, because the model configures it explicitly rather than relying on
    /// convention over public properties.
    /// </summary>
    internal ICollection<RefreshToken> RefreshTokens { get; } = [];
}
