using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Domain.Core.Common.Abstractions;

/// <summary>
/// An aggregate that belongs to one subject, so every read and every write of it is answerable
/// only to that subject.
/// <para>
/// The owner is a <see cref="UserId"/> and nothing else: the business knows who owns a thing and
/// nothing about who authenticated, which is what lets an application have owned data without
/// having authentication at all.
/// </para>
/// </summary>
public interface IOwnedAggregate
{
    /// <summary>Whose it is. Assigned when the aggregate is created and never afterwards changed.</summary>
    UserId OwnerId { get; }
}
