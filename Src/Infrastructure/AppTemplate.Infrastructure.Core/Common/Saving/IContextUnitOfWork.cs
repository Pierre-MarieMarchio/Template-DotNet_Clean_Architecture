using AppTemplate.Application.Core.Common.Ports;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving;

/// <summary>The commit boundary of one named context.</summary>
/// <remarks><see cref="IUnitOfWork"/> says nothing about <em>which</em> context it commits — right
/// for a use case, wrong for a deployment holding more than one: the unnamed port can be registered
/// only once, and a module staging writes on a second context would have them committed by the first,
/// a failure that is silent because <c>SaveChangesAsync</c> on an empty context succeeds and reports
/// zero rows. So it is designed out here. A module that owns a context takes this; a use case takes
/// <see cref="IUnitOfWork"/>, which the module owning the business context registers over its
/// own.</remarks>
/// <typeparam name="TContext">The context whose staged changes this commits.</typeparam>
public interface IContextUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext;
