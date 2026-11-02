namespace AppTemplate.Api.Features.Maintenance.Contracts.Responses;

/// <summary>The outcome of a purge, shared by every purge endpoint.</summary>
/// <param name="Deleted">Rows removed by this call.</param>
public sealed record PurgeResponse(int Deleted);
