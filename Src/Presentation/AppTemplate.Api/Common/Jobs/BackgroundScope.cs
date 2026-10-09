using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Api.Common.Jobs;

/// <summary>
/// Says whether the current scope was opened by a background job rather than by a request.
/// <para>
/// A request and a job resolve the same use cases from one container, so what tells them apart has
/// to be per scope: the caller and the audit actor are registered once and answer by asking this.
/// A job that opened its scope any other way would be taken for a request with no principal, which
/// reads as an anonymous caller instead of failing — hence <see cref="BackgroundScopeExtensions.CreateBackgroundScope"/>
/// is the only way the jobs open one.
/// </para>
/// </summary>
internal sealed class BackgroundScope
{
    /// <summary>True once <see cref="BackgroundScopeExtensions.CreateBackgroundScope"/> has marked this scope.</summary>
    public bool IsActive { get; private set; }

    internal void Enter() => IsActive = true;
}

internal static class BackgroundScopeExtensions
{
    /// <summary>Registers the marker. Safe to call twice: the caller identity and the jobs both need it.</summary>
    internal static IServiceCollection AddBackgroundScope(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<BackgroundScope>();

        return services;
    }

    /// <summary>
    /// A scope for one pass of a job: <c>ICurrentUser</c> refuses to answer in it and the audit
    /// actor records nobody. Dispose it when the pass ends, as with any scope.
    /// </summary>
    internal static AsyncServiceScope CreateBackgroundScope(this IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundScope>().Enter();

        return scope;
    }
}
