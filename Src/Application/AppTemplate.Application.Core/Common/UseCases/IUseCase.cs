namespace AppTemplate.Application.Core.Common.UseCases;

/// <summary>
/// Registration discovers use cases through this marker rather than by name, so a rename cannot
/// silently drop one from the container.
/// </summary>
public interface IUseCase;

/// <summary>One operation, taking the whole of its explicit input as a single request object.</summary>
public interface IUseCase<TRequest, TResponse> : IUseCase
{
    /// <summary>
    /// The whole of the operation in one call, so there is no sequence for a caller to get wrong and no
    /// state left behind between calls.
    /// </summary>
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One operation whose whole input is ambient — the caller's identity, the clock.</summary>
public interface IUseCase<TResponse> : IUseCase
{
    /// <summary>Runs the operation; everything it needs it resolves for itself.</summary>
    Task<TResponse> ExecuteAsync(CancellationToken cancellationToken = default);
}
