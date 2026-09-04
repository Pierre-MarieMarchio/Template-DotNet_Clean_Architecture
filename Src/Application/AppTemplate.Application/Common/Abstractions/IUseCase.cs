namespace AppTemplate.Application.Common.Abstractions;

/// <summary>
/// Registration discovers use cases through this marker rather than by name, so a rename cannot
/// silently drop one from the container.
/// </summary>
public interface IUseCase;

public interface IUseCase<TRequest, TResponse> : IUseCase
{
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One operation whose whole input is ambient — the caller's identity, the clock.</summary>
public interface IUseCase<TResponse> : IUseCase
{
    Task<TResponse> ExecuteAsync(CancellationToken cancellationToken = default);
}
