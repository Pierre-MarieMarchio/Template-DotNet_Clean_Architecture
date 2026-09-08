using AppTemplate.Application.Core.Common.Ports;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving;

/// <summary>
/// The commit boundary of one named context.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IUnitOfWork"/> says nothing about <em>which</em> context it commits, which is exactly
/// right for a use case and exactly wrong for a deployment holding more than one: a container can
/// register the unnamed port only once, and a module whose writes were staged on a second context
/// would have them committed by the first. That failure is silent —
/// <c>SaveChangesAsync</c> on a context with nothing tracked succeeds and reports zero rows — so it
/// is designed out here rather than watched for.
/// </para>
/// <para>
/// A module that owns a context takes this; a use case takes <see cref="IUnitOfWork"/>, which the
/// module owning the business context registers over its own.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The context whose staged changes this commits.</typeparam>
public interface IContextUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext;
