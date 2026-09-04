using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Infrastructure.Identity.IntegrationTests.Fixtures;

/// <summary>
/// The one adapter the host supplies rather than a module. Auditing needs it to resolve a context at
/// all, and nothing under test writes an audited row.
/// </summary>
internal sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
}
